namespace iD_Develops.Services
{
    public sealed class ScheduleOccurrenceGeneratorService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScheduleOccurrenceGeneratorService> _logger;

        public ScheduleOccurrenceGeneratorService(
            IServiceScopeFactory scopeFactory,
            ILogger<ScheduleOccurrenceGeneratorService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var schedulingService = scope.ServiceProvider.GetRequiredService<ISchedulingService>();
                    await schedulingService.GenerateUpcomingEventsAsync(cancellationToken: stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Generating recurring schedule events failed.");
                }

                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }
    }
}
