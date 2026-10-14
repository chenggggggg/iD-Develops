using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed class ZoomAttendanceReconciliationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDependencyStatusStore _dependencyStatus;
        private readonly ILogger<ZoomAttendanceReconciliationWorker> _logger;

        public ZoomAttendanceReconciliationWorker(
            IServiceScopeFactory scopeFactory,
            IDependencyStatusStore dependencyStatus,
            ILogger<ZoomAttendanceReconciliationWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _dependencyStatus = dependencyStatus;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMinutes(5);
                try
                {
                    await _dependencyStatus.WaitForDatabaseAsync(stoppingToken);
                    using var scope = _scopeFactory.CreateScope();
                    var attendanceService = scope.ServiceProvider.GetRequiredService<IZoomAttendanceService>();
                    await attendanceService.ReconcileDueMeetingsAsync(stoppingToken);
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
                            "Zoom attendance reconciliation paused because the database is unavailable. Retry delay: {RetryDelay}.",
                            delay);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Zoom attendance reconciliation is waiting for database recovery. Retry delay: {RetryDelay}.",
                            delay);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Reconciling Zoom attendance failed.");
                }

                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
