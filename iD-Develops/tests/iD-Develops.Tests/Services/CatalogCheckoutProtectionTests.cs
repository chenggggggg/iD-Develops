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
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed));
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.FulfillmentFailed));
        Assert.False(await data.Db.ApplicationAuditEvents.AnyAsync(row => row.EventType == ApplicationAuditEventTypes.PurchaseCompleted));
        await data.Fulfillment.CompleteAsync(data.Stripe.Session);
        await data.Fulfillment.CompleteAsync(data.Stripe.Session);
        Assert.Single(await data.Db.ProductFormSubmissions.ToListAsync());
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed));
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.PurchaseCompleted));
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
            Options.Create(new StripeSettings { WebhookSecret = "whsec_test" }), data.Fulfillment, data.Access, data.Db, data.CheckoutAudit);
        for (var index = 0; index < 2; index++)
        {
            var context = data.Context();
            context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            context.Request.Headers["Stripe-Signature"] = $"t={timestamp},v1={hash}";
            var result = await webhook.CheckoutCompletedAsync(context.Request);
            Assert.NotEqual(500, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        }
        Assert.Equal(expected, await data.Db.ProductFormSubmissions.CountAsync());
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == (expected == 1 ? ApplicationAuditEventTypes.PaymentConfirmed : ApplicationAuditEventTypes.PaymentPending)));
    }

    [Fact]
    public async Task InvalidWebhookSignatureCannotCreateAnOrder()
    {
        using var data = new TestCheckout();
        var webhook = new StripeWebhookService(NullLogger<StripeWebhookService>.Instance,
            Options.Create(new StripeSettings { WebhookSecret = "whsec_test" }), data.Fulfillment, data.Access, data.Db, data.CheckoutAudit);
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
        var failure = await Assert.ThrowsAsync<CatalogFulfillmentException>(() => data.Fulfillment.CompleteAsync(session));
        Assert.Equal("payment_mismatch", failure.Code);
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
        var dispatcher = new CatalogCheckoutMailDispatcher(data.Db, data.Mail, data.MailSettings, NullLogger<CatalogCheckoutMailDispatcher>.Instance, data.CheckoutAudit);
        await dispatcher.DispatchAsync(default);
        Assert.Single(await data.Db.ProductFormSubmissions.ToListAsync());
        Assert.Equal(1, await data.Db.CatalogCheckoutMails.CountAsync(row => row.SentAtUtc != null));
        await data.Db.CatalogCheckoutMails.ExecuteUpdateAsync(update => update.SetProperty(row => row.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        Assert.Equal(3, data.Mail.Calls);
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.NotificationFailed));
        Assert.Equal(2, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.NotificationAccepted));
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.PurchaseCompleted));
        Assert.Equal(2, await data.Db.CatalogCheckoutMails.CountAsync(row => row.SentAtUtc != null));
    }

    [Theory]
    [InlineData("checkout.session.expired")]
    [InlineData("checkout.session.async_payment_failed")]
    public async Task DelayedFailureCannotCancelVerifiedPaymentEvenWhenDeliveryFailed(string eventType)
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "paid";
        data.Credits.FailNext = true;
        Assert.Equal(500, await WebhookAsync(data, session, "checkout.session.completed", "evt_paid"));
        session.PaymentStatus = "unpaid";
        Assert.Equal(200, await WebhookAsync(data, session, eventType, "evt_late_failure"));
        Assert.False(await data.Db.ApplicationAuditEvents.AnyAsync(row => row.EventType == ApplicationAuditEventTypes.PurchaseFailed));
        Assert.Single(await data.Db.ApplicationAuditEvents.Where(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed).ToListAsync());
        session.PaymentStatus = "paid";
        Assert.Equal(200, await WebhookAsync(data, session, "checkout.session.completed", "evt_paid"));
        Assert.Single(await data.Db.CatalogCheckoutReceipts.ToListAsync());
    }

    [Fact]
    public async Task DelayedPaymentHasPendingThenConfirmedThenDeliveryEvents()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new() { ["private"] = "Do not copy form data into audit logs" });
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "unpaid";
        session.PaymentIntentId = "pi_reconcile";
        Assert.Equal(200, await WebhookAsync(data, session, "checkout.session.completed", "evt_pending"));
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        session.PaymentStatus = "paid";
        Assert.Equal(200, await WebhookAsync(data, session, "checkout.session.async_payment_succeeded", "evt_confirmed"));
        Assert.Equal(200, await WebhookAsync(data, session, "checkout.session.async_payment_succeeded", "evt_confirmed"));
        var rows = await data.Db.ApplicationAuditEvents.Where(row => row.Category == AuditEventCategory.Commerce).OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(new[] { ApplicationAuditEventTypes.CheckoutRequested, ApplicationAuditEventTypes.CheckoutResponseReceived, ApplicationAuditEventTypes.CheckoutCallbackReceived, ApplicationAuditEventTypes.PaymentPending, ApplicationAuditEventTypes.CheckoutCallbackReceived, ApplicationAuditEventTypes.PaymentConfirmed, ApplicationAuditEventTypes.PurchaseCompleted }, rows.Select(row => row.EventType));
        var metadata = System.Text.Json.JsonDocument.Parse(rows[5].MetadataJson!).RootElement;
        Assert.Equal(session.Id, metadata.GetProperty("stripeSessionId").GetString());
        Assert.Equal("pi_reconcile", metadata.GetProperty("stripePaymentIntentId").GetString());
        Assert.Equal("evt_confirmed", metadata.GetProperty("stripeEventId").GetString());
        Assert.Equal(session.AmountTotal, metadata.GetProperty("amountTotalMinorUnits").GetInt64());
        Assert.Equal(session.Currency, metadata.GetProperty("currency").GetString());
        Assert.DoesNotContain("Do not copy form data", string.Join(" ", rows.Select(row => row.MetadataJson)));
    }

    [Theory]
    [InlineData("checkout.session.expired")]
    [InlineData("checkout.session.async_payment_failed")]
    public async Task UnpaidFailureIsAuditedOnceAndLaterPaymentCanRecover(string eventType)
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "unpaid";
        Assert.Equal(200, await WebhookAsync(data, session, eventType, "evt_failed"));
        Assert.Equal(200, await WebhookAsync(data, session, eventType, "evt_failed"));
        Assert.Single(await data.Db.ApplicationAuditEvents.Where(row => row.EventType == ApplicationAuditEventTypes.PurchaseFailed).ToListAsync());
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        session.PaymentStatus = "paid";
        Assert.Equal(200, await WebhookAsync(data, session, "checkout.session.async_payment_succeeded", "evt_recovered"));
        Assert.Single(await data.Db.CatalogCheckoutReceipts.ToListAsync());
    }

    [Fact]
    public async Task InvalidSignedPayloadCannotRecordPaymentOrGrantAccess()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "paid";
        Assert.Equal(400, await WebhookAsync(data, session, "checkout.session.completed", "evt_forged", validSignature: false));
        Assert.False(await data.Db.ApplicationAuditEvents.AnyAsync(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed || row.EventType == ApplicationAuditEventTypes.PurchaseCompleted));
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
    }

    [Fact]
    public async Task MissingCoursePurchaserRemainsUndeliveredAndRecoversAfterAccountIsRestored()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var course = new Course { Name = "Purchased course" };
        data.Db.Courses.Add(course);
        await data.Db.SaveChangesAsync();
        product.GrantedCourseId = course.Id;
        await data.Db.SaveChangesAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "paid";
        Assert.Equal(500, await WebhookAsync(data, session, "checkout.session.completed", "evt_course"));
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        Assert.Empty(await data.Db.UserCourses.ToListAsync());
        var error = await data.Db.ApplicationAuditEvents.SingleAsync(row => row.EventType == ApplicationAuditEventTypes.FulfillmentFailed);
        Assert.Contains("purchaser_missing", error.MetadataJson);
        var attempt = await data.Db.CatalogCheckoutAttempts.SingleAsync();
        data.Db.Users.Add(new ApplicationUser
        {
            Id = "restored",
            UserName = attempt.CustomerEmail,
            Email = attempt.CustomerEmail,
            NormalizedEmail = attempt.CustomerEmail!.ToUpperInvariant()
        });
        await data.Db.SaveChangesAsync();
        Assert.Equal(200, await WebhookAsync(data, session, "checkout.session.completed", "evt_course"));
        Assert.Equal(200, await WebhookAsync(data, session, "checkout.session.completed", "evt_course"));
        Assert.Single(await data.Db.UserCourses.ToListAsync());
        Assert.Single(await data.Db.CatalogCheckoutReceipts.ToListAsync());
    }

    [Fact]
    public async Task MissingCreditPurchaserDoesNotMarkTheProductDelivered()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var credit = new CreditType { Name = "Private lesson", NormalizedName = "PRIVATE LESSON" };
        data.Db.CreditTypes.Add(credit);
        await data.Db.SaveChangesAsync();
        data.Db.CatalogProductCreditGrants.Add(new() { CatalogProductId = product.Id, CreditTypeId = credit.Id });
        await data.Db.SaveChangesAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "paid";
        Assert.Equal(500, await WebhookAsync(data, session, "checkout.session.completed", "evt_credit"));
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        Assert.Empty(await data.Db.UserCreditLots.ToListAsync());
        Assert.Single(await data.Db.ApplicationAuditEvents.Where(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed).ToListAsync());
        Assert.Single(await data.Db.ApplicationAuditEvents.Where(row => row.EventType == ApplicationAuditEventTypes.FulfillmentFailed).ToListAsync());
    }

    [Fact]
    public async Task AuditFailureReturns500BeforeGrantingAndSuccessfulRetryKeepsOneReceipt()
    {
        using var data = new TestCheckout(failPaymentAudit: true);
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "paid";
        Assert.Equal(500, await WebhookAsync(data, session, "checkout.session.completed", "evt_audit_retry"));
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        Assert.Empty(await data.Db.ProductFormSubmissions.ToListAsync());
        Assert.Equal(200, await WebhookAsync(data, session, "checkout.session.completed", "evt_audit_retry"));
        Assert.Single(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        Assert.Single(await data.Db.ApplicationAuditEvents.Where(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed).ToListAsync());
    }

    [Theory]
    [InlineData("invalid", "product_reference_invalid")]
    [InlineData("2147483647", "product_missing")]
    public async Task InvalidProductReferenceKeepsPaymentProofAndReturnsRetryableDeliveryFailure(string productId, string code)
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        var session = data.Stripe.Session!;
        session.Status = "complete";
        session.PaymentStatus = "paid";
        session.Metadata["catalog_product_id"] = productId;
        Assert.Equal(500, await WebhookAsync(data, session, "checkout.session.completed", "evt_invalid_product"));
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        Assert.Single(await data.Db.ApplicationAuditEvents.Where(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed).ToListAsync());
        var failure = await data.Db.ApplicationAuditEvents.SingleAsync(row => row.EventType == ApplicationAuditEventTypes.FulfillmentFailed);
        Assert.Contains(code, failure.MetadataJson);
    }

    [Fact]
    public async Task CheckoutRequestAndResponseAreRecordedBeforePaymentAndDeduplicateRetries()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var context = data.Context();
        var requestId = Guid.NewGuid();
        await data.StartAsync(context, product, requestId, new());
        await data.StartAsync(context, product, requestId, new());
        var rows = await data.Db.ApplicationAuditEvents.OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(new[] { ApplicationAuditEventTypes.CheckoutRequested, ApplicationAuditEventTypes.CheckoutResponseReceived }, rows.Select(row => row.EventType));
        Assert.Single(await data.Db.CatalogCheckoutAttempts.ToListAsync());
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
        Assert.Contains(data.Stripe.Session!.Id, rows[1].MetadataJson);
    }

    [Fact]
    public async Task LostCheckoutResponseLeavesAnAppAttemptWithoutClaimingPaymentFailed()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        var context = data.Context();
        var requestId = Guid.NewGuid();
        data.Stripe.FailNext = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => data.StartAsync(context, product, requestId, new()));
        var rows = await data.Db.ApplicationAuditEvents.OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(new[] { ApplicationAuditEventTypes.CheckoutRequested, ApplicationAuditEventTypes.CheckoutSessionCreationFailed }, rows.Select(row => row.EventType));
        Assert.Contains("payment outcome is unknown", rows[1].Summary);
        await data.StartAsync(context, product, requestId, new());
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.CheckoutRequested));
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.CheckoutResponseReceived));
        Assert.False(await data.Db.ApplicationAuditEvents.AnyAsync(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed || row.EventType == ApplicationAuditEventTypes.PurchaseFailed));
    }

    [Fact]
    public async Task BrowserReturnIsOnlyAnObservationAndUnknownReferencesDoNotCreateRecords()
    {
        using var data = new TestCheckout();
        var product = await data.ProductAsync();
        await data.StartAsync(data.Context(), product, Guid.NewGuid(), new());
        await data.CheckoutAudit.BrowserReturnedAsync(data.Stripe.Session!.Id, default);
        await data.CheckoutAudit.BrowserReturnedAsync(data.Stripe.Session!.Id, default);
        await data.CheckoutAudit.BrowserReturnedAsync("cs_unknown", default);
        Assert.Equal(1, await data.Db.ApplicationAuditEvents.CountAsync(row => row.EventType == ApplicationAuditEventTypes.CheckoutBrowserReturned));
        Assert.False(await data.Db.ApplicationAuditEvents.AnyAsync(row => row.EventType == ApplicationAuditEventTypes.PaymentConfirmed));
        Assert.Empty(await data.Db.CatalogCheckoutReceipts.ToListAsync());
    }

    private static async Task<int?> WebhookAsync(TestCheckout data, Session session, string eventType, string eventId, bool validSignature = true)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = eventId,
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
                    status = session.Status,
                    payment_status = session.PaymentStatus,
                    payment_intent = session.PaymentIntentId,
                    amount_total = session.AmountTotal,
                    currency = session.Currency,
                    metadata = session.Metadata
                }
            }
        });
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(validSignature ? "whsec_test" : "whsec_wrong"));
        var hash = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{timestamp}.{json}"))).ToLowerInvariant();
        var context = data.Context();
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        context.Request.Headers["Stripe-Signature"] = $"t={timestamp},v1={hash}";
        var service = new StripeWebhookService(NullLogger<StripeWebhookService>.Instance,
            Options.Create(new StripeSettings { WebhookSecret = "whsec_test" }), data.Fulfillment, data.Access, data.Db, data.CheckoutAudit);
        return Assert.IsAssignableFrom<IStatusCodeHttpResult>(await service.CheckoutCompletedAsync(context.Request)).StatusCode ?? StatusCodes.Status200OK;
    }

    private sealed class RejectPaymentAuditOnce : SaveChangesInterceptor
    {
        private bool _fail = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_fail && eventData.Context!.ChangeTracker.Entries<ApplicationAuditEvent>().Any(row => row.Entity.EventType == ApplicationAuditEventTypes.PaymentConfirmed))
            {
                _fail = false;
                throw new InvalidOperationException("Simulated audit storage failure.");
            }
            return ValueTask.FromResult(result);
        }
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
        public CatalogCheckoutAuditService CheckoutAudit { get; }
        public CatalogCheckoutFulfillmentService Fulfillment { get; }
        public FailOnceCredits Credits { get; }
        private readonly CatalogCheckoutCoordinator _coordinator;

        public TestCheckout(bool failPaymentAudit = false)
        {
            Db = _factory.CreateDbContext();
            var auditOptions = new DbContextOptionsBuilder<iD_Develops.Data.ApplicationDbContext>(_factory.Options);
            if (failPaymentAudit) auditOptions.AddInterceptors(new RejectPaymentAuditOnce());
            CheckoutAudit = new(auditOptions.Options, new HttpContextAccessor());
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
                urls, CheckoutAudit, NullLogger<CatalogCheckoutFulfillmentService>.Instance);
            _coordinator = new(Db, new CatalogStripeCheckoutService(Stripe), Stripe, Access, new TestEnvironment(), Fulfillment, urls, CheckoutAudit);
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
        public bool FailNext { get; set; }
        public SessionCreateOptions? Options { get; private set; }
        public Session? Session { get; private set; }
        public Task<Session> CreateAsync(SessionCreateOptions options, string idempotencyKey, CancellationToken ct)
        {
            Creates++;
            if (FailNext)
            {
                FailNext = false;
                throw new HttpRequestException("Simulated lost checkout response.");
            }
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
        public Task<OperationResult> SendAsync<T>(string path, T model, string recipient, string subject, byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default, string? idempotencyKey = null)
            => SendAsync(path, model, recipient, subject, null, attachment, attachmentFileName, ct);
        public Task<OperationResult> SendAsync<T>(string path, T model, string recipient, string subject, string? fromEmail,
            byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default, string? idempotencyKey = null)
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
