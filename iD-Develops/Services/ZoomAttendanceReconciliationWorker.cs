namespace iD_Develops.Services
{
    public sealed class ZoomAttendanceReconciliationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ZoomAttendanceReconciliationWorker> _logger;

        public ZoomAttendanceReconciliationWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<ZoomAttendanceReconciliationWorker> logger)
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
                    var attendanceService = scope.ServiceProvider.GetRequiredService<IZoomAttendanceService>();
                    await attendanceService.ReconcileDueMeetingsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Reconciling Zoom attendance failed.");
                }

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
