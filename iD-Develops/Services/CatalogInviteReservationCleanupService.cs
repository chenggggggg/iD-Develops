using iD_Develops.Configuration;
using iD_Develops.Utilities;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services;

public sealed class CatalogInviteReservationCleanupService : BackgroundService
{
    private const string OperationKey = "catalog-invite-reservations";
    private const string OperationName = "Checkout reservation cleanup";
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    private readonly IServiceProvider _serviceProvider;
    private readonly IDependencyStatusStore _dependencyStatus;
    private readonly IBackgroundOperationStatusStore _operationStatus;
    private readonly BackgroundCleanupOptions _options;
    private readonly ILogger<CatalogInviteReservationCleanupService> _logger;

    public CatalogInviteReservationCleanupService(
        IServiceProvider serviceProvider,
        IDependencyStatusStore dependencyStatus,
        IBackgroundOperationStatusStore operationStatus,
        IOptions<BackgroundCleanupOptions> options,
        ILogger<CatalogInviteReservationCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _dependencyStatus = dependencyStatus;
        _operationStatus = operationStatus;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var normalInterval = TimeSpan.FromMinutes(
            Math.Clamp(_options.InviteReservationIntervalMinutes, 1, 1440));
        var currentDelay = normalInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _dependencyStatus.WaitForDatabaseAsync(stoppingToken);
                var startedAtUtc = DateTime.UtcNow;

                using var scope = _serviceProvider.CreateScope();
                var accessService = scope.ServiceProvider.GetRequiredService<CatalogProductAccessService>();
                var expiredCount = await accessService.ExpirePendingUsesAsync(stoppingToken);
                var completedAtUtc = DateTime.UtcNow;
                var message = expiredCount == 0
                    ? "No abandoned checkout reservations needed to be expired."
                    : $"Expired {expiredCount} abandoned checkout reservation(s).";
                _operationStatus.ReportSuccess(
                    OperationKey,
                    OperationName,
                    startedAtUtc,
                    completedAtUtc,
                    completedAtUtc.Add(normalInterval),
                    expiredCount,
                    message);
                if (expiredCount > 0)
                {
                    _logger.LogInformation(
                        "Invite-reservation cleanup expired {ExpiredCount} abandoned checkout reservation(s).",
                        expiredCount);
                }

                currentDelay = normalInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (DatabaseAvailability.IsDatabaseUnavailable(exception))
            {
                var stateChanged = _dependencyStatus.ReportDatabaseUnavailable(
                    DatabaseAvailability.GetDatabaseErrorCode(exception));
                currentDelay = MinBackoff;
                _operationStatus.ReportWaiting(
                    OperationKey,
                    OperationName,
                    DateTime.UtcNow.Add(currentDelay),
                    "Waiting for the database to become available.");

                if (stateChanged)
                {
                    _logger.LogWarning(
                        "Invite-reservation cleanup paused because the database is unavailable. Retry delay: {RetryDelay}.",
                        currentDelay);
                }
                else
                {
                    _logger.LogDebug(
                        "Invite-reservation cleanup is waiting for database recovery. Retry delay: {RetryDelay}.",
                        currentDelay);
                }
            }
            catch (Exception exception)
            {
                currentDelay = TimeSpan.FromSeconds(
                    Math.Min(
                        MaxBackoff.TotalSeconds,
                        Math.Max(MinBackoff.TotalSeconds, currentDelay.TotalSeconds * 2)));
                _operationStatus.ReportFailure(
                    OperationKey,
                    OperationName,
                    DateTime.UtcNow.Add(currentDelay),
                    "The cleanup run failed and will be retried.");
                _logger.LogWarning(
                    exception,
                    "Invite-reservation cleanup failed. Retry delay: {RetryDelay}.",
                    currentDelay);
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
}
