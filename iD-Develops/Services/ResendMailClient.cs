using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using iD_Develops.Configuration;
using iD_Develops.Utilities;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services;

public sealed class ResendMailClient(HttpClient httpClient, IOptions<MailSettings> options, ILogger<ResendMailClient> logger)
{
    public async Task<OperationResult> SendAsync(string from, string recipient, string subject, string html,
        byte[]? attachment, string? attachmentFileName, string? idempotencyKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(options.Value.ResendApiKey))
        {
            logger.LogError("Mail:ResendApiKey must be configured for Resend mail.");
            return Failed();
        }

        // Render and serialize once so all attempts use exactly the same body and key.
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            from = $"iD! Training & Development <{from}>",
            to = new[] { recipient },
            subject,
            html,
            attachments = attachment != null && !string.IsNullOrWhiteSpace(attachmentFileName)
                ? new[] { new { filename = attachmentFileName, content = Convert.ToBase64String(attachment) } }
                : null
        }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        var key = idempotencyKey ?? $"mail/{Guid.NewGuid():N}";
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ResendApiKey);
                request.Headers.Add("Idempotency-Key", key);
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await httpClient.SendAsync(request, timeout.Token);
                ApiResponse? result = null;
                try { result = await response.Content.ReadFromJsonAsync<ApiResponse>(cancellationToken: timeout.Token); }
                catch (JsonException) { /* Classify non-JSON failures by HTTP status as well. */ }
                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(result?.Id))
                    {
                        logger.LogError("Resend returned success without an email ID.");
                        return Failed();
                    }
                    logger.LogInformation("Resend accepted email {EmailId} from {Sender} for recipient domain {RecipientDomain}.",
                        result.Id, from, recipient.Split('@').Last());
                    return new OperationResult { Success = true, ErrorMessage = "Email accepted for delivery." };
                }

                // Do not expose response messages: they can contain recipient details.
                logger.LogWarning("Resend rejected email with HTTP {StatusCode} and code {ErrorCode}.",
                    (int)response.StatusCode, result?.Name);
                var retryable = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (response.StatusCode == HttpStatusCode.Conflict && result?.Name == "concurrent_idempotent_requests");
                if (!retryable || result?.Name is "daily_quota_exceeded" or "monthly_quota_exceeded") return Failed();
                var retryAfter = response.Headers.RetryAfter;
                if (retryAfter?.Delta is { } delta) delay = delta;
                else if (retryAfter?.Date is { } date) delay = date - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.FromSeconds(30)) return Failed(); // The queue can retry later.
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
            {
                logger.LogWarning("Resend email request failed with {ExceptionType} on attempt {Attempt}.",
                    exception.GetType().Name, attempt + 1);
            }
            if (attempt < 3) await Task.Delay(delay, ct);
        }
        return Failed();
    }

    private static OperationResult Failed() => new() { Success = false, ErrorMessage = "Email delivery could not be confirmed." };
    private sealed class ApiResponse
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }
}
