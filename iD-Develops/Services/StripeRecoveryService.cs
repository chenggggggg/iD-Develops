using iD_Develops.Configuration;
using iD_Develops.Utilities;
using Microsoft.Extensions.Options;
using Stripe;

namespace iD_Develops.Services;

public sealed class StripeRecoveryService : BackgroundService
{
    private readonly IDependencyStatusStore _statusStore;
    private readonly StripeSettings _stripeSettings;
    private readonly DependencyRecoveryOptions _recoveryOptions;
    private readonly ILogger<StripeRecoveryService> _logger;

    public StripeRecoveryService(
        IDependencyStatusStore statusStore,
        IOptions<StripeSettings> stripeSettings,
        IOptions<DependencyRecoveryOptions> recoveryOptions,
        ILogger<StripeRecoveryService> logger)
    {
        _statusStore = statusStore;
        _stripeSettings = stripeSettings.Value;
        _recoveryOptions = recoveryOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        if (!_stripeSettings.Enabled)
        {
            _statusStore.ReportStripeDisabled();
            _logger.LogInformation("Stripe availability monitoring is disabled because Stripe is not configured.");
            return;
        }

        var retryAttempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = StripeProbeInterval;

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(StripeAttemptTimeout);

                await new BalanceService().GetAsync(cancellationToken: timeout.Token);
                retryAttempt = 0;

                if (_statusStore.ReportStripeHealthy())
                {
                    _logger.LogInformation("Stripe is available.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                delay = GetRetryDelay(retryAttempt++);
                ReportUnavailable("stripe-timeout", retryAttempt, delay);
            }
            catch (StripeException exception)
            {
                delay = GetRetryDelay(retryAttempt++);
                ReportUnavailable(
                    ExternalDependencyAvailability.GetStripeErrorCode(exception),
                    retryAttempt,
                    delay);
            }
            catch (HttpRequestException)
            {
                delay = GetRetryDelay(retryAttempt++);
                ReportUnavailable("stripe-offline", retryAttempt, delay);
            }
            catch (Exception exception)
            {
                delay = GetRetryDelay(retryAttempt++);
                var stateChanged = _statusStore.ReportStripeUnavailable("stripe-check-failed");
                if (stateChanged)
                {
                    _logger.LogError(
                        exception,
                        "Stripe availability check failed. Retry attempt: {Attempt}. Next retry: {RetryDelay}.",
                        retryAttempt,
                        delay);
                }
                else
                {
                    _logger.LogDebug(
                        "Stripe availability check is still failing. Retry attempt: {Attempt}. Next retry: {RetryDelay}. Error type: {ExceptionType}.",
                        retryAttempt,
                        delay,
                        exception.GetType().Name);
                }
            }

            await DelayAsync(delay, stoppingToken);
        }
    }

    private void ReportUnavailable(string errorCode, int attempt, TimeSpan retryDelay)
    {
        if (_statusStore.ReportStripeUnavailable(errorCode))
        {
            _logger.LogWarning(
                "Stripe is unavailable. Error: {ErrorCode}. Retry attempt: {Attempt}. Next retry: {RetryDelay}.",
                errorCode,
                attempt,
                retryDelay);
        }
        else
        {
            _logger.LogDebug(
                "Stripe remains unavailable. Error: {ErrorCode}. Retry attempt: {Attempt}. Next retry: {RetryDelay}.",
                errorCode,
                attempt,
                retryDelay);
        }
    }

    private TimeSpan GetRetryDelay(int retryAttempt)
    {
        var initialRetry = TimeSpan.FromSeconds(Math.Max(1, _recoveryOptions.InitialRetrySeconds));
        var maximumRetry = TimeSpan.FromSeconds(
            Math.Max(initialRetry.TotalSeconds, _recoveryOptions.MaximumRetrySeconds));
        var exponent = Math.Min(retryAttempt, 10);
        var seconds = initialRetry.TotalSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(maximumRetry.TotalSeconds, seconds));
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

    private TimeSpan StripeProbeInterval => TimeSpan.FromSeconds(
        Math.Max(30, _recoveryOptions.StripeProbeIntervalSeconds));

    private TimeSpan StripeAttemptTimeout => TimeSpan.FromSeconds(
        Math.Max(1, _recoveryOptions.StripeAttemptTimeoutSeconds));
}
