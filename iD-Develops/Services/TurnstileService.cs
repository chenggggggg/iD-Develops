using System.Text.Json;
using System.Text.Json.Serialization;
using iD_Develops.Configuration;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services;

public sealed class TurnstileService(HttpClient httpClient, IOptions<TurnstileSettings> options,
    ILogger<TurnstileService> logger) : ITurnstileService
{
    public const string ResponseFieldName = "cf-turnstile-response";
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private readonly TurnstileSettings _settings = options.Value;
    public bool IsEnabled => _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.SiteKey) && !string.IsNullOrWhiteSpace(_settings.SecretKey);

    public async Task<bool> ValidateAsync(HttpContext context, ModelStateDictionary modelState, CancellationToken ct = default)
    {
        if (!_settings.Enabled) return true;
        if (!modelState.IsValid) return false; // Do not consume a single-use token for an invalid form.
        if (!IsEnabled)
        {
            logger.LogWarning("Turnstile is enabled but its site key or secret key is missing.");
            return Reject("Security verification is not configured. Please try again later.");
        }
        var token = context.Request.Form[ResponseFieldName].ToString();
        if (string.IsNullOrWhiteSpace(token)) return Reject("Please complete the security verification.");
        if (token.Length > 2048) return Reject("Security verification failed. Please complete the check again.");
        var values = new Dictionary<string, string>
        {
            ["secret"] = _settings.SecretKey!,
            ["response"] = token,
            ["idempotency_key"] = Guid.NewGuid().ToString()
        };
        // remoteip is optional. Proxy/private addresses are not reliable visitor identities.
        // The signed challenge token remains mandatory for every submission.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var response = await httpClient.PostAsync(SiteVerifyUrl, new FormUrlEncodedContent(values), timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Turnstile validation failed with HTTP status {StatusCode}.", response.StatusCode);
                    if (attempt == 0 && (int)response.StatusCode >= 500) continue;
                    return Reject("Security verification is unavailable. Please try again.");
                }
                var result = await response.Content.ReadFromJsonAsync<VerifyResponse>(cancellationToken: timeout.Token);
                if (result?.Success == true) return true;
                var errors = result?.ErrorCodes ?? [];
                logger.LogWarning("Turnstile validation rejected request. Errors: {Errors}", string.Join(", ", errors));
                if (attempt == 0 && errors.Contains("internal-error")) continue;
                if (errors.Contains("timeout-or-duplicate"))
                    return Reject("The security check expired or was already used. Please complete it again.");
                if (errors.Contains("invalid-input-secret") || errors.Contains("missing-input-secret"))
                    return Reject("Security verification is unavailable. Please try again later.");
                return Reject("Security verification failed. Please complete the check again.");
            }
            catch (Exception exception) when (!ct.IsCancellationRequested && exception is HttpRequestException or OperationCanceledException or JsonException)
            {
                logger.LogWarning(exception, "Turnstile validation request failed.");
                if (attempt == 0) continue;
            }
        }
        return Reject("Security verification could not be completed. Please try again.");

        bool Reject(string message)
        {
            modelState.AddModelError(ResponseFieldName, message);
            return false;
        }
    }

    private sealed class VerifyResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("error-codes")] public string[]? ErrorCodes { get; set; }
    }
}
