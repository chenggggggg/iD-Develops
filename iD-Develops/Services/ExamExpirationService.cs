namespace iD_Develops.Services
{
    public class ExamExpirationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExamExpirationService> _logger;

        // Normal cadence when everything is healthy
        private static readonly TimeSpan NormalInterval = TimeSpan.FromMinutes(1);

        // Backoff when failures occur (DB down, transient issues, etc.)
        private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

        public ExamExpirationService(
            IServiceProvider serviceProvider,
            ILogger<ExamExpirationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var currentDelay = NormalInterval;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForOverdueExamsAsync(stoppingToken);

                    // Success => reset delay to normal cadence
                    currentDelay = NormalInterval;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // normal shutdown
                }
                catch (Exception ex)
                {
                    // Keep running even if DB is down / transient failures occur.
                    _logger.LogWarning(ex,
                        "ExamExpirationService failed to mark expired records overdue. Will retry in {Delay}.",
                        currentDelay);

                    // Failure => backoff (up to max)
                    currentDelay = TimeSpan.FromSeconds(
                        Math.Min(MaxBackoff.TotalSeconds, Math.Max(MinBackoff.TotalSeconds, currentDelay.TotalSeconds * 2)));
                }

                try
                {
                    await Task.Delay(currentDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task CheckForOverdueExamsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();

            var recordService = scope.ServiceProvider.GetRequiredService<IRecordService>();
            await recordService.MarkExpiredRecordsOverdueAsync(stoppingToken);
        }
    }
}

