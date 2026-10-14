using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed class ScheduleOccurrenceGeneratorService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDependencyStatusStore _dependencyStatus;
        private readonly ILogger<ScheduleOccurrenceGeneratorService> _logger;

        public ScheduleOccurrenceGeneratorService(
            IServiceScopeFactory scopeFactory,
            IDependencyStatusStore dependencyStatus,
            ILogger<ScheduleOccurrenceGeneratorService> logger)
        {
            _scopeFactory = scopeFactory;
            _dependencyStatus = dependencyStatus;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromHours(6);
                try
                {
                    await _dependencyStatus.WaitForDatabaseAsync(stoppingToken);
                    using var scope = _scopeFactory.CreateScope();
                    var schedulingService = scope.ServiceProvider.GetRequiredService<ISchedulingService>();
                    await schedulingService.GenerateUpcomingEventsAsync(cancellationToken: stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (DatabaseAvailability.IsDatabaseUnavailable(exception))
                {
                    var stateChanged = _dependencyStatus.ReportDatabaseUnavailable(
                        DatabaseAvailability.GetDatabaseErrorCode(exception));
                    delay = TimeSpan.FromSeconds(30);
                    if (stateChanged)
                    {
                        _logger.LogWarning(
                            "Recurring-event generation paused because the database is unavailable. Retry delay: {RetryDelay}.",
                            delay);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Recurring-event generation is waiting for database recovery. Retry delay: {RetryDelay}.",
                            delay);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Generating recurring schedule events failed.");
                }

                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
