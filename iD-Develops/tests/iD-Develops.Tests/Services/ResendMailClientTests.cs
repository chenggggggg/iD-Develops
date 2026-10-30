using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using iD_Develops.Configuration;
using iD_Develops.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class ResendMailClientTests
{
    [Fact]
    public async Task SendsHtmlAndAttachmentThroughResendWithExistingSenderName()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Accepted()));
        using var http = new HttpClient(handler);
        var client = Create(http);
        var result = await client.SendAsync("billing@example.test", "customer@example.test", "Your receipt", "<p>Receipt</p>",
            [1, 2, 3], "receipt.pdf", "notification/123", default);
        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.resend.com/emails", request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Equal("Bearer test-only-key", request.Authorization);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("notification/123", request.Key);
        using var json = JsonDocument.Parse(request.Body);
        var body = json.RootElement;
        Assert.Equal("iD! Training & Development <billing@example.test>", body.GetProperty("from").GetString());
        Assert.Equal("customer@example.test", body.GetProperty("to")[0].GetString());
        Assert.Equal("Your receipt", body.GetProperty("subject").GetString());
        Assert.Equal("<p>Receipt</p>", body.GetProperty("html").GetString());
        Assert.Equal("receipt.pdf", body.GetProperty("attachments")[0].GetProperty("filename").GetString());
        Assert.Equal("AQID", body.GetProperty("attachments")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task RetriesTransientFailureWithIdenticalBodyAndKeyButSeparateMailGetsANewKey()
    {
        var calls = 0;
        using var handler = new RecordingHandler((_, _) => Task.FromResult(++calls == 1
            ? Error(HttpStatusCode.ServiceUnavailable, "service_unavailable") : Accepted()));
        using var http = new HttpClient(handler);
        var client = Create(http);
        Assert.True((await Send(client)).Success);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Key, handler.Requests[1].Key);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        using var json = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.False(json.RootElement.TryGetProperty("attachments", out _));
        Assert.True((await Send(client)).Success);
        Assert.NotEqual(handler.Requests[0].Key, handler.Requests[2].Key);
    }

    [Theory]
    [InlineData(429, "rate_limit_exceeded")]
    [InlineData(409, "concurrent_idempotent_requests")]
    public async Task RetriesRateLimitsAndConcurrentRequestsWithTheSameExplicitKey(int status, string code)
    {
        var calls = 0;
        using var handler = new RecordingHandler((_, _) => Task.FromResult(++calls == 1 ? Error((HttpStatusCode)status, code) : Accepted()));
        using var http = new HttpClient(handler);
        var client = Create(http);
        Assert.True((await Send(client, key: "queued/456")).Success);
        Assert.True((await Send(client, key: "queued/456")).Success);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("queued/456", request.Key));
    }

    [Theory]
    [InlineData(401, "invalid_api_key")]
    [InlineData(403, "validation_error")]
    [InlineData(422, "validation_error")]
    [InlineData(409, "invalid_idempotent_request")]
    [InlineData(429, "daily_quota_exceeded")]
    [InlineData(429, "monthly_quota_exceeded")]
    public async Task PermanentFailureIsNotRetriedOrExposedToTheCaller(int status, string code)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Error((HttpStatusCode)status, code)));
        using var http = new HttpClient(handler);
        var result = await Send(Create(http));
        Assert.False(result.Success);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("private@example.test", result.ErrorMessage);
        Assert.DoesNotContain("test-only-key", result.ErrorMessage);
    }

    [Theory]
    [InlineData(401, 1)]
    [InlineData(502, 4)]
    public async Task NonJsonFailuresRespectHttpStatus(int status, int expectedCalls)
    {
        using var handler = new RecordingHandler((_, _) =>
        {
            var response = Error((HttpStatusCode)status, "unused");
            response.Content = new StringContent("<html>upstream error</html>", Encoding.UTF8, "text/html");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        Assert.False((await Send(Create(http))).Success);
        Assert.Equal(expectedCalls, handler.Requests.Count);
    }

    [Fact]
    public async Task ExtendedRetryAfterReturnsFailureSoQueueCanRetryLater()
    {
        using var handler = new RecordingHandler((_, _) =>
        {
            var response = Error(HttpStatusCode.TooManyRequests, "rate_limit_exceeded");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        Assert.False((await Send(Create(http))).Success);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostResponseOrRequestTimeoutCanRetrySafely(bool timeout)
    {
        var calls = 0;
        using var handler = new RecordingHandler((_, _) =>
        {
            if (++calls == 1) throw timeout ? new OperationCanceledException() : new HttpRequestException("Response lost");
            return Task.FromResult(Accepted());
        });
        using var http = new HttpClient(handler);
        Assert.True((await Send(Create(http))).Success);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Key, handler.Requests[1].Key);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    public async Task SuccessWithoutProviderIdDoesNotMarkMailAsSent(string payload)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler);
        Assert.False((await Send(Create(http))).Success);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MissingApiKeyDoesNotSendARequest()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Accepted()));
        using var http = new HttpClient(handler);
        Assert.False((await Send(Create(http, " "))).Success);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationStopsBeforeSending()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Accepted()));
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(Create(http), cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationInterruptsRetryDelay()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(Error(HttpStatusCode.ServiceUnavailable, "service_unavailable"));
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(Create(http), cancellation.Token));
        Assert.Single(handler.Requests);
    }

    private static ResendMailClient Create(HttpClient http, string? key = "test-only-key")
        => new(http, Options.Create(new MailSettings { ResendApiKey = key }), NullLogger<ResendMailClient>.Instance);
    private static Task<iD_Develops.Utilities.OperationResult> Send(ResendMailClient client, CancellationToken ct = default, string? key = null)
        => client.SendAsync("info@example.test", "customer@example.test", "Subject", "<p>Hello</p>", null, null, key, ct);
    private static HttpResponseMessage Accepted() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"id\":\"provider-email-id\"}", Encoding.UTF8, "application/json")
    };
    private static HttpResponseMessage Error(HttpStatusCode status, string code)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { name = code, message = "private@example.test rejected" }), Encoding.UTF8, "application/json")
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }
    private sealed record SentRequest(string Url, string Method, string? Authorization, string? ContentType, string Key, string Body);
    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.RequestUri!.ToString(), request.Method.Method, request.Headers.Authorization?.ToString(),
                request.Content!.Headers.ContentType?.MediaType, request.Headers.GetValues("Idempotency-Key").Single(),
                await request.Content.ReadAsStringAsync(cancellationToken)));
            return await send(request, cancellationToken);
        }
    }
}
