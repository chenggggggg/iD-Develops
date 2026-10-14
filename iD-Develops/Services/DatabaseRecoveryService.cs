using iD_Develops.Configuration;
using iD_Develops.Utilities;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services;

public sealed class DatabaseRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDependencyStatusStore _statusStore;
    private readonly DependencyRecoveryOptions _options;
    private readonly ILogger<DatabaseRecoveryService> _logger;

    public DatabaseRecoveryService(
        IServiceScopeFactory scopeFactory,
        IDependencyStatusStore statusStore,
        IOptions<DependencyRecoveryOptions> options,
        ILogger<DatabaseRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _statusStore = statusStore;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var retryAttempt = 0;
        var initialized = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (initialized)
                {
                    initialized = await ProbeDatabaseAsync(stoppingToken);
                    if (initialized)
                    {
                        if (_statusStore.ReportDatabaseHealthy())
                        {
                            _logger.LogInformation("Database connection recovered.");
                        }

                        await DelayAsync(DatabaseProbeInterval, stoppingToken);
                        continue;
                    }

                    ReportUnavailable("db-offline", retryAttempt + 1, InitialRetryDelay);
                }
                else
                {
                    await InitializeDatabaseAsync(stoppingToken);
                    initialized = true;
                    retryAttempt = 0;

                    if (_statusStore.ReportDatabaseHealthy())
                    {
                        _logger.LogInformation(
                            "Database is available. Migrations and configured identity data are up to date.");
                    }

                    await DelayAsync(DatabaseProbeInterval, stoppingToken);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                initialized = false;
                ReportUnavailable("db-timeout", retryAttempt + 1, GetRetryDelay(retryAttempt));
            }
            catch (Exception exception) when (DatabaseAvailability.IsDatabaseUnavailable(exception) || exception is TimeoutException)
            {
                initialized = false;
                ReportUnavailable(
                    DatabaseAvailability.GetDatabaseErrorCode(exception),
                    retryAttempt + 1,
                    GetRetryDelay(retryAttempt));
            }
            catch (Exception exception)
            {
                initialized = false;
                var delay = GetRetryDelay(retryAttempt);
                var stateChanged = _statusStore.ReportDatabaseUnavailable("db-initialization-failed");
                if (stateChanged)
                {
                    _logger.LogError(
                        exception,
                        "Database initialization failed. Retry attempt: {Attempt}. Next retry: {RetryDelay}.",
                        retryAttempt + 1,
                        delay);
                }
                else
                {
                    _logger.LogDebug(
                        "Database initialization is still failing. Retry attempt: {Attempt}. Next retry: {RetryDelay}. Error type: {ExceptionType}.",
                        retryAttempt + 1,
                        delay,
                        exception.GetType().Name);
                }
            }

            var retryDelay = GetRetryDelay(retryAttempt);
            retryAttempt++;
            await DelayAsync(retryDelay, stoppingToken);
        }
    }

    private async Task InitializeDatabaseAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var recoveryAttempt = scope.ServiceProvider.GetRequiredService<IDatabaseRecoveryAttempt>();
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            timeout.CancelAfter(DatabaseAttemptTimeout);
            if (!await recoveryAttempt.CanConnectAsync(timeout.Token))
            {
                throw new TimeoutException("The database connection check did not succeed.");
            }
        }

        await recoveryAttempt.InitializeAsync(stoppingToken);
    }

    private async Task<bool> ProbeDatabaseAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(DatabaseAttemptTimeout);

        using var scope = _scopeFactory.CreateScope();
        var recoveryAttempt = scope.ServiceProvider.GetRequiredService<IDatabaseRecoveryAttempt>();
        return await recoveryAttempt.CanConnectAsync(timeout.Token);
    }

    private void ReportUnavailable(string errorCode, int attempt, TimeSpan retryDelay)
    {
        if (_statusStore.ReportDatabaseUnavailable(errorCode))
        {
            _logger.LogWarning(
                "Database is unavailable. Error: {ErrorCode}. Retry attempt: {Attempt}. Next retry: {RetryDelay}.",
                errorCode,
                attempt,
                retryDelay);
        }
        else
        {
            _logger.LogDebug(
                "Database remains unavailable. Error: {ErrorCode}. Retry attempt: {Attempt}. Next retry: {RetryDelay}.",
                errorCode,
                attempt,
                retryDelay);
        }
    }

    private TimeSpan GetRetryDelay(int retryAttempt)
    {
        var exponent = Math.Min(retryAttempt, 10);
        var seconds = InitialRetryDelay.TotalSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(MaximumRetryDelay.TotalSeconds, seconds));
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private TimeSpan InitialRetryDelay => TimeSpan.FromSeconds(Math.Max(1, _options.InitialRetrySeconds));

    private TimeSpan MaximumRetryDelay => TimeSpan.FromSeconds(
        Math.Max(InitialRetryDelay.TotalSeconds, _options.MaximumRetrySeconds));

    private TimeSpan DatabaseProbeInterval => TimeSpan.FromSeconds(
        Math.Max(5, _options.DatabaseProbeIntervalSeconds));

    private TimeSpan DatabaseAttemptTimeout => TimeSpan.FromSeconds(
        Math.Max(1, _options.DatabaseAttemptTimeoutSeconds));
}
