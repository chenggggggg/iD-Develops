using System.Text.Json.Serialization;
using iD_Develops.Configuration;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services
{
    public sealed class TurnstileService : ITurnstileService
    {
        private const string ResponseFieldName = "cf-turnstile-response";
        private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

        private readonly HttpClient _httpClient;
        private readonly TurnstileSettings _settings;
        private readonly ILogger<TurnstileService> _logger;

        public TurnstileService(
            HttpClient httpClient,
            IOptions<TurnstileSettings> settings,
            ILogger<TurnstileService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public bool IsEnabled
            => _settings.Enabled &&
               !string.IsNullOrWhiteSpace(_settings.SiteKey) &&
               !string.IsNullOrWhiteSpace(_settings.SecretKey);

        public async Task<bool> ValidateAsync(HttpContext httpContext, ModelStateDictionary modelState, CancellationToken ct = default)
        {
            if (!_settings.Enabled)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_settings.SecretKey))
            {
                _logger.LogWarning("Turnstile is enabled but Turnstile:SecretKey is missing.");
                modelState.AddModelError(string.Empty, "Security verification is not configured. Please try again later.");
                return false;
            }

            var token = httpContext.Request.Form[ResponseFieldName].ToString();
            if (string.IsNullOrWhiteSpace(token))
            {
                modelState.AddModelError(string.Empty, "Please complete the security verification.");
                return false;
            }

            var formValues = new Dictionary<string, string>
            {
                ["secret"] = _settings.SecretKey,
                ["response"] = token
            };

            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrWhiteSpace(remoteIp))
            {
                formValues["remoteip"] = remoteIp;
            }

            try
            {
                using var response = await _httpClient.PostAsync(SiteVerifyUrl, new FormUrlEncodedContent(formValues), ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Turnstile validation failed with HTTP status {StatusCode}.", response.StatusCode);
                    modelState.AddModelError(string.Empty, "Security verification failed. Please try again.");
                    return false;
                }

                var result = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>(cancellationToken: ct);
                if (result?.Success == true)
                {
                    return true;
                }

                _logger.LogWarning(
                    "Turnstile validation rejected request. Errors: {Errors}",
                    string.Join(", ", result?.ErrorCodes ?? Array.Empty<string>()));
                modelState.AddModelError(string.Empty, "Security verification failed. Please try again.");
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                _logger.LogWarning(ex, "Turnstile validation request failed.");
                modelState.AddModelError(string.Empty, "Security verification could not be completed. Please try again.");
                return false;
            }
        }

        private sealed class TurnstileVerifyResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("error-codes")]
            public string[] ErrorCodes { get; set; } = Array.Empty<string>();
        }
    }
}
