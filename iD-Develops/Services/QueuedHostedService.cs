using Polly;
using Polly.Retry;

namespace iD_Develops.Services
{
    public class QueuedHostedService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<QueuedHostedService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        private static readonly TimeSpan DequeueFailureBackoff = TimeSpan.FromSeconds(5);

        public QueuedHostedService(IBackgroundTaskQueue taskQueue, IServiceScopeFactory scopeFactory, ILogger<QueuedHostedService> logger)
        {
            _taskQueue = taskQueue;
            _scopeFactory = scopeFactory;
            _logger = logger;

            // Retry individual work items (transient failures, DB down, etc.)
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(2000 * Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, _) =>
                    {
                        _logger.LogWarning(exception,
                            "Background work item retry {RetryCount} after {Delay}.",
                            retryCount,
                            timeSpan);
                    });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("QueuedHostedService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                Func<CancellationToken, Task>? workItem = null;

                try
                {
                    workItem = await _taskQueue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // normal shutdown
                }
                catch (Exception ex)
                {
                    // If the queue itself fails (rare), don't let it kill the host.
                    _logger.LogWarning(ex, "Failed to dequeue background work item. Will retry in {Delay}.", DequeueFailureBackoff);

                    try
                    {
                        await Task.Delay(DequeueFailureBackoff, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    // Execute the work item with retry policy
                    await _retryPolicy.ExecuteAsync(async token => await workItem(token), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing background work item.");
                }
            }

            _logger.LogInformation("QueuedHostedService is stopping.");
        }
    }
}
