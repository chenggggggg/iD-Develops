using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Pages;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Threading.RateLimiting;
using Stripe.Checkout;

namespace iD_Develops.Tests.Services;

public sealed class CatalogCheckoutProtectionTests
{
    [Fact]
    public async Task InvalidSignupFieldsDoNotConsumeTheSecurityToken()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        product.ProductType = CatalogProductType.Signup;
        product.WorkflowType = CatalogWorkflowType.FormSubmission;
        product.FormFields.Add(new() { Key = "name", Label = "Name", IsRequired = true });
        await data.Db.SaveChangesAsync();
        var turnstile = new RejectingTurnstile();
        Assert.IsType<PageResult>(await data.Page(turnstile).OnPostAsync(product.Slug));
        Assert.Equal(0, turnstile.Calls);
    }
    [Fact]
    public async Task LegacyOrderAlreadyRecordedInAuditIsNotFulfilledAgain()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var audit = new ApplicationAuditService(data.Db, new HttpContextAccessor(), NullLogger<ApplicationAuditService>.Instance);
        await audit.RecordAsync(new ApplicationAuditEntry(ApplicationAuditEventTypes.PurchaseCompleted,
            AuditEventCategory.Commerce, AuditEventOutcome.Success, "Previously completed.",
            Metadata: new Dictionary<string, object?> { ["stripeSessionId"] = "cs_already_paid" }));
        await data.Fulfillment.CompleteAsync(new Session
        {
            Id = "cs_already_paid",
            Status = "complete",
            PaymentStatus = "paid",
            Metadata = new() { ["catalog_product_id"] = product.Id.ToString() }
        });
        Assert.Empty(await data.Db.ProductFormSubmissions.ToListAsync());
        Assert.Empty(await data.Db.CatalogCheckoutMails.ToListAsync());
        Assert.Single(await data.Db.CatalogCheckoutReceipts.ToListAsync());
    }
    [Fact]
    public async Task CheckoutOwnerCookieReusesPendingOrderAcrossRequestsWithoutSessionState()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var first = data.Context();
        var requestId = Guid.NewGuid();
        await data.StartAsync(first, product, requestId, new());
        var header = first.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        var owner = (string)first.Items["Catalog.CheckoutOwner"]!;
        var next = data.Context(owner);
        await data.StartAsync(next, product, requestId, new());
        Assert.Equal(1, data.Stripe.Creates);
    }
    [Fact]
    public async Task FailedFulfillmentRollsBackAndCanBeRetriedWithoutDuplicateSubmission()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        data.Stripe.Session!.Status = "complete";
        data.Stripe.Session.PaymentStatus = "paid";
        data.Credits.FailNext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Fulfillment.CompleteAsync(data.Stripe.Session));
        Assert.Empty(await data.Db.ProductFormSubmissions.AsNoTracking().ToListAsync());
        Assert.Empty(await data.Db.CatalogCheckoutMails.AsNoTracking().ToListAsync());
        await data.Fulfillment.CompleteAsync(data.Stripe.Session);
        Assert.Single(await data.Db.ProductFormSubmissions.ToListAsync());
    }

    [Theory]
    [InlineData("checkout.session.completed", "unpaid", 0)]
    [InlineData("checkout.session.completed", "paid", 1)]
    [InlineData("checkout.session.async_payment_succeeded", "paid", 1)]
    public async Task SignedWebhooksRequireConfirmedPaymentAndDeduplicate(string eventType, string paymentStatus, int expected)
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = "evt_test",
            @object = "event",
            type = eventType,
            api_version = "2024-06-20",
            created = timestamp,
            request = (object?)null,
            data = new
            {
                @object = new
                {
                    id = session.Id,
                    @object = "checkout.session",
                    status = "complete",
                    payment_status = paymentStatus,
                    amount_total = session.AmountTotal,
                    currency = session.Currency,
                    metadata = session.Metadata
                }
            }
        });
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("whsec_test"));
        var hash = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{timestamp}.{json}"))).ToLowerInvariant();
        var webhook = new StripeWebhookService(NullLogger<StripeWebhookService>.Instance,
            Options.Create(new StripeSettings { WebhookSecret = "whsec_test" }), data.Fulfillment, data.Access, data.Db, new ApplicationAuditService(data.Db, new HttpContextAccessor(), NullLogger<ApplicationAuditService>.Instance));
        for (var index = 0; index < 2; index++)
        {
            var context = data.Context();
            context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            context.Request.Headers["Stripe-Signature"] = $"t={timestamp},v1={hash}";
            var result = await webhook.CheckoutCompletedAsync(context.Request);
            Assert.NotEqual(500, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        }
        Assert.Equal(expected, await data.Db.ProductFormSubmissions.CountAsync());
    }

    [Fact]
    public async Task InvalidWebhookSignatureCannotCreateAnOrder()
    {
        using var data = new TestCheckout();
        var webhook = new StripeWebhookService(NullLogger<StripeWebhookService>.Instance,
            Options.Create(new StripeSettings { WebhookSecret = "whsec_test" }), data.Fulfillment, data.Access, data.Db, new ApplicationAuditService(data.Db, new HttpContextAccessor(), NullLogger<ApplicationAuditService>.Instance));
        var context = data.Context();
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));
        var result = await webhook.CheckoutCompletedAsync(context.Request);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Empty(await data.Db.ProductFormSubmissions.ToListAsync());
    }

    [Theory]
    [InlineData(CatalogWorkflowType.FormThenStripeCheckout, 0)]
    [InlineData(CatalogWorkflowType.FormSubmission, 1)]
    public async Task BlockedTurnstileCannotBlockPaidCheckoutButStillProtectsUnpaidForms(CatalogWorkflowType workflow, int expectedChecks)
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        product.WorkflowType = workflow;
        await data.Db.SaveChangesAsync();
        var turnstile = new RejectingTurnstile();
        var page = data.Page(turnstile);
        var result = await page.OnPostAsync(product.Slug);
        Assert.Equal(expectedChecks, turnstile.Calls);
        if (expectedChecks == 0) Assert.IsType<RedirectResult>(result);
        else Assert.IsType<PageResult>(result);
    }

    [Fact]
    public void RateLimitRestrictsRepeatedRequestsWithoutBlockingPageNavigation()
    {
        using var data = new TestCheckout();
        var context = data.Context();
        context.Request.Method = "POST";
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(ProductCheckoutRateLimit.Partition);
        for (var index = 0; index < 10; index++) Assert.True(limiter.AttemptAcquire(context).IsAcquired);
        Assert.False(limiter.AttemptAcquire(context).IsAcquired);
        context.Request.Method = "GET";
        Assert.True(limiter.AttemptAcquire(context).IsAcquired);
        context.Request.Host = new("portal.example.test");
        context.Request.Method = "POST";
        Assert.True(limiter.AttemptAcquire(context).IsAcquired);
    }

    [Fact]
    public async Task RepeatedCheckoutReusesSessionAndKeepsLongFormDetailsOnServer()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var context = data.Context();
        var id = Guid.NewGuid();
        var values = new Dictionary<string, string> { ["message"] = new('x', 2000) };
        var first = await data.StartAsync(context, product, id, values);
        var second = await data.StartAsync(context, product, id, values);
        Assert.Equal(first, second);
        Assert.Equal(1, data.Stripe.Creates);
        Assert.Single(await data.Db.CatalogCheckoutAttempts.ToListAsync());
        Assert.Empty(await data.Db.ProductFormSubmissions.ToListAsync());
        Assert.Empty(await data.Db.CatalogCheckoutMails.ToListAsync());
        Assert.DoesNotContain("form_data", data.Stripe.Options!.Metadata.Keys);
        Assert.Contains("checkout_attempt_id", data.Stripe.Options.Metadata.Keys);
        Assert.Contains(values["message"], (await data.Db.CatalogCheckoutAttempts.SingleAsync()).FormDataJson);
    }

    [Fact]
    public async Task DifferentBrowsersAndChangedDetailsDoNotReuseAnotherCheckout()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var id = Guid.NewGuid();
        var firstBrowser = data.Context();
        await data.StartAsync(firstBrowser, product, id, new() { ["name"] = "First" });
        await data.StartAsync(data.Context(), product, id, new() { ["name"] = "First" });
        await data.StartAsync(firstBrowser, product, id, new() { ["name"] = "Changed" });
        Assert.Equal(3, data.Stripe.Creates);
    }

    [Fact]
    public async Task PrivateCheckoutRetryReservesOnlyOneInviteUse()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        product.RequireAccessToken = true;
        await data.Db.SaveChangesAsync();
        var invite = await data.Access.CreateInviteAsync(product.Id, "Private", null, 1, null);
        var id = Guid.NewGuid();
        var context = data.Context();
        await data.StartAsync(context, product, id, new(), invite.Token);
        await data.StartAsync(context, product, id, new(), invite.Token);
        Assert.Single(await data.Db.CatalogProductInviteUses.ToListAsync());
        Assert.Equal(1, data.Stripe.Creates);
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.StartAsync(data.Context(), product, id, new(), "invalid"));
        Assert.Single(await data.Db.CatalogCheckoutAttempts.ToListAsync());
    }

    [Fact]
    public async Task UnpaidCheckoutDoesNothingThenPaidRetriesFulfillOnlyOnce()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new() { ["message"] = "Saved" });
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "unpaid";
        Assert.False(await data.Fulfillment.CompleteAsync(session));
        Assert.Empty(await data.Db.ProductFormSubmissions.ToListAsync());
        session.PaymentStatus = "paid";
        Assert.True(await data.Fulfillment.CompleteAsync(session));
        Assert.True(await data.Fulfillment.CompleteAsync(session));
        Assert.Single(await data.Db.ProductFormSubmissions.ToListAsync());
        Assert.Single(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        Assert.Equal(2, await data.Db.CatalogCheckoutMails.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncorrectAmountOrCurrencyCannotFulfill(bool wrongCurrency)
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "paid";
        if (wrongCurrency) session.Currency = "usd";
        else session.AmountTotal++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Fulfillment.CompleteAsync(session));
        Assert.Empty(await data.Db.ProductFormSubmissions.ToListAsync());
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
    }

    [Fact]
    public async Task FullyDiscountedCheckoutAcceptsVerifiedNoPaymentRequired()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        product.DiscountType = CatalogDiscountType.Percentage;
        product.DiscountValue = 100;
        await data.Db.SaveChangesAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "no_payment_required";
        Assert.True(await data.Fulfillment.CompleteAsync(session));
        Assert.Single(await data.Db.ProductFormSubmissions.ToListAsync());
    }

    [Fact]
    public async Task LegacyPaidSessionsStillCompleteAndDeduplicate()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var session = new Session
        {
            Id = "cs_legacy",
            Status = "complete",
            PaymentStatus = "paid",
            Metadata = new() { ["catalog_product_id"] = product.Id.ToString(), ["form_data"] = "{\"name\":\"Legacy\"}" }
        };
        await data.Fulfillment.CompleteAsync(session);
        await data.Fulfillment.CompleteAsync(session);
        Assert.Single(await data.Db.ProductFormSubmissions.ToListAsync());
    }

    [Fact]
    public async Task MailFailureDoesNotUndoOrderAndRetriesOnlyUnsentNotification()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        data.Stripe.Session!.Status = "complete";
        data.Stripe.Session.PaymentStatus = "paid";
        await data.Fulfillment.CompleteAsync(data.Stripe.Session);
        data.Mail.FailNext = true;
        var dispatcher = new CatalogCheckoutMailDispatcher(data.Db, data.Mail, data.MailSettings, NullLogger<CatalogCheckoutMailDispatcher>.Instance);
        await dispatcher.DispatchAsync(default);
        Assert.Single(await data.Db.ProductFormSubmissions.ToListAsync());
        Assert.Equal(1, await data.Db.CatalogCheckoutMails.CountAsync(row => row.SentAtUtc != null));
        await data.Db.CatalogCheckoutMails.ExecuteUpdateAsync(update => update.SetProperty(row => row.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        Assert.Equal(3, data.Mail.Calls);
        Assert.Equal(2, await data.Db.CatalogCheckoutMails.CountAsync(row => row.SentAtUtc != null));
    }

    private sealed class TestCheckout : IDisposable
    {
        private readonly SqliteTestDbFactory _factory = new();
        private readonly ServiceProvider _services;
        public ApplicationDbContext Db { get; }
        public FakeStripe Stripe { get; } = new();
        public RecordingMail Mail { get; } = new();
        public IOptions<MailSettings> MailSettings { get; } = Options.Create(new MailSettings { To = "owner@example.test" });
        public CatalogProductAccessService Access { get; }
        public CatalogCheckoutFulfillmentService Fulfillment { get; }
        public FailOnceCredits Credits { get; }
        private readonly CatalogCheckoutCoordinator _coordinator;

        public TestCheckout()
        {
            Db = _factory.CreateDbContext();
            var urls = new ApplicationUrlService(Options.Create(new ApplicationUrlOptions
            {
                PublicBaseUrl = "https://public.example.test",
                PortalBaseUrl = "https://portal.example.test"
            }));
            Access = new(Db);
            var submission = new CatalogProductSubmissionService(new ProductFormSubmissionService(Db), MailSettings, new(MailSettings));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddSingleton<IApplicationUrlService>(urls);
            _services = services.BuildServiceProvider();
            Credits = new(new PurchasedCreditService(Db, NullLogger<PurchasedCreditService>.Instance));
            Fulfillment = new(Db, new CatalogProductService(Db), submission, Access, Credits,
                new ApplicationAuditService(Db, new HttpContextAccessor(), NullLogger<ApplicationAuditService>.Instance),
                urls, NullLogger<CatalogCheckoutFulfillmentService>.Instance);
            _coordinator = new(Db, new CatalogStripeCheckoutService(Stripe), Stripe, Access, new TestEnvironment(), Fulfillment, urls, new ApplicationAuditService(Db, new HttpContextAccessor(), NullLogger<ApplicationAuditService>.Instance));
        }

        public async Task<CatalogProduct> ProductAsync()
        {
            var product = new CatalogProduct
            {
                Name = "Checkout",
                Slug = "checkout",
                BasePrice = 10,
                WorkflowType = CatalogWorkflowType.FormThenStripeCheckout,
                Status = CatalogProductStatus.Published,
                Variants = [new() { Name = "Standard", Price = 10, Currency = "EUR", IsActive = true, IsDefault = true }]
            };
            Db.CatalogProducts.Add(product);
            await Db.SaveChangesAsync();
            return product;
        }

        public DefaultHttpContext Context(string? owner = null)
        {
            var context = new DefaultHttpContext { RequestServices = _services };
            context.Request.Scheme = "https";
            context.Request.Host = new("public.example.test");
            if (owner != null) context.Request.Headers.Cookie = "Catalog.CheckoutOwner=" + owner;
            context.Features.Set<ISessionFeature>(new SessionFeature { Session = new TestSession() });
            CatalogCheckoutCoordinator.GetOwner(context);
            return context;
        }

        public ProductModel Page(ITurnstileService turnstile)
        {
            var context = Context();
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
            return new ProductModel(new CatalogProductService(Db), null!, null!, _coordinator, Access,
                new CatalogProductFileStorageService(new TestEnvironment(), new ConfigurationBuilder().Build(), new DisabledPresignedUrlService(), null!),
                new TestEnvironment(), turnstile, _services.GetRequiredService<IApplicationUrlService>(), null!,
                new ApplicationAuditService(Db, new HttpContextAccessor(), NullLogger<ApplicationAuditService>.Instance),
                _services.GetRequiredService<IStringLocalizer<ProductModel>>())
            {
                PageContext = new PageContext { HttpContext = context }
            };
        }

        public Task<string> StartAsync(HttpContext context, CatalogProduct product, Guid id, Dictionary<string, string> values, string? access = null)
            => _coordinator.StartAsync(context, product, product.Variants.First(), id, values, "buyer@example.test", 1, 1, access, null, default);
        public void Dispose() { _services.Dispose(); Db.Dispose(); _factory.Dispose(); }
    }

    private sealed class FakeStripe : IStripeCheckoutClient
    {
        public int Creates { get; private set; }
        public SessionCreateOptions? Options { get; private set; }
        public Session? Session { get; private set; }
        public Task<Session> CreateAsync(SessionCreateOptions options, string idempotencyKey, CancellationToken ct)
        {
            Creates++;
            Options = options;
            Session = new Session
            {
                Id = $"cs_test_{Creates}",
                Url = $"https://checkout.stripe.test/{Creates}",
                Status = "open",
                PaymentStatus = "unpaid",
                Currency = options.LineItems[0].PriceData.Currency,
                AmountTotal = options.LineItems.Sum(item => item.PriceData.UnitAmount * item.Quantity),
                Metadata = options.Metadata
            };
            return Task.FromResult(Session);
        }
        public Task<Session> GetAsync(string sessionId, CancellationToken ct) => Task.FromResult(Session!);
    }

    private sealed class RecordingMail : IMailService
    {
        public int Calls { get; private set; }
        public bool FailNext { get; set; }
        public Task<OperationResult> SendAsync<T>(string path, T model, string recipient, string subject, byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default)
            => SendAsync(path, model, recipient, subject, null, attachment, attachmentFileName, ct);
        public Task<OperationResult> SendAsync<T>(string path, T model, string recipient, string subject, string? fromEmail,
            byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default)
        {
            Calls++;
            var success = !FailNext;
            FailNext = false;
            return Task.FromResult(new OperationResult { Success = success });
        }
    }

    private sealed class FailOnceCredits(IPurchasedCreditService actual) : IPurchasedCreditService
    {
        public bool FailNext { get; set; }
        public Task GrantPurchasedCreditsAsync(int productId, string? userId, string? email, string reference, int quantity, CancellationToken ct = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Simulated entitlement failure.");
            }
            return actual.GrantPurchasedCreditsAsync(productId, userId, email, reference, quantity, ct);
        }
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Staging";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RejectingTurnstile : ITurnstileService
    {
        public int Calls { get; private set; }
        public bool IsEnabled => true;
        public Task<bool> ValidateAsync(HttpContext context, ModelStateDictionary state, CancellationToken ct = default)
        {
            Calls++;
            state.AddModelError(string.Empty, "Turnstile is blocked.");
            return Task.FromResult(false);
        }
    }
}
