using System.Net;
using iD_Develops.Configuration;
using iD_Develops.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class TurnstileServiceTests
{
    [Fact]
    public async Task SuccessIsVerifiedWithServerSecretAndRetryIdentifier()
    {
        using var handler = new VerificationHandler("{\"success\":true}");
        var state = new ModelStateDictionary();
        Assert.True(await Service(handler).ValidateAsync(Context("fresh-token"), state));
        Assert.Single(handler.Requests);
        Assert.Contains("response=fresh-token", handler.Requests[0]);
        Assert.Contains("idempotency_key=", handler.Requests[0]);
        Assert.DoesNotContain("remoteip", handler.Requests[0]);
    }

    [Fact]
    public async Task InvalidFieldsDoNotConsumeTheSingleUseToken()
    {
        using var handler = new VerificationHandler();
        var state = new ModelStateDictionary();
        state.AddModelError("email", "Email is required.");
        Assert.False(await Service(handler).ValidateAsync(Context("fresh-token"), state));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("timeout-or-duplicate", "expired or was already used")]
    [InlineData("invalid-input-secret", "unavailable")]
    [InlineData("invalid-input-response", "complete the check again")]
    public async Task DefinitiveRejectionsAreNotRetriedAndAppearBesideTheWidget(string code, string expected)
    {
        using var handler = new VerificationHandler("{\"success\":false,\"error-codes\":[\"" + code + "\"]}");
        var state = new ModelStateDictionary();
        Assert.False(await Service(handler).ValidateAsync(Context("used-token"), state));
        Assert.Single(handler.Requests);
        Assert.Contains(expected, state[TurnstileService.ResponseFieldName]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task TemporaryFailureRetriesWithTheSameIdempotencyKey()
    {
        using var handler = new VerificationHandler("{\"success\":false,\"error-codes\":[\"internal-error\"]}", "{\"success\":true}");
        Assert.True(await Service(handler).ValidateAsync(Context("fresh-token"), new()));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0], handler.Requests[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("oversized")]
    public async Task MissingOrOversizedTokensAreRejectedWithoutCallingCloudflare(string kind)
    {
        using var handler = new VerificationHandler();
        Assert.False(await Service(handler).ValidateAsync(Context(kind == "" ? "" : new string('x', 2049)), new()));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("site", null)]
    public async Task IncompleteConfigurationFailsClosed(string? site, string? secret)
    {
        using var handler = new VerificationHandler();
        var service = new TurnstileService(new HttpClient(handler), Options.Create(new TurnstileSettings
        {
            Enabled = true,
            SiteKey = site,
            SecretKey = secret
        }), NullLogger<TurnstileService>.Instance);
        Assert.False(service.IsEnabled);
        Assert.False(await service.ValidateAsync(Context("token"), new()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MalformedResponsesNeverAcceptAForm()
    {
        using var handler = new VerificationHandler("not-json", "not-json");
        Assert.False(await Service(handler).ValidateAsync(Context("token"), new()));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutRetry()
    {
        using var handler = new VerificationHandler("{\"success\":true}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(handler).ValidateAsync(Context("token"), new(), cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    private static TurnstileService Service(VerificationHandler handler) => new(new HttpClient(handler),
        Options.Create(new TurnstileSettings { Enabled = true, SiteKey = "test-site", SecretKey = "test-secret" }),
        NullLogger<TurnstileService>.Instance);

    private static DefaultHttpContext Context(string token)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            [TurnstileService.ResponseFieldName] = token
        });
        return context;
    }

    private sealed class VerificationHandler(params string[] responses) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses[Requests.Count - 1]) };
        }
    }
}
