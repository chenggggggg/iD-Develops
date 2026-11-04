using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class CatalogRefundTests
{
    [Theory]
    [InlineData("Local", "pi_external", false)]
    [InlineData("Local", "local_payment_demo", true)]
    [InlineData("Staging", "local_payment_demo", false)]
    public async Task RefundTransportKeepsLocalSimulationAndExternalPaymentsIsolated(string environment, string paymentId, bool supported)
    {
        await using var data = await Data.CreateAsync();
        var client = new StripeRefundClient(Options.Create(new StripeSettings { Enabled = true, SecretKey = "sk_test_not_a_real_key" }),
            new RefundEnvironment { EnvironmentName = environment }, data.Db);
        Assert.Equal(supported, client.SupportsPayment(paymentId));
        if (!supported)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListAsync(paymentId, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateAsync(paymentId, 2500, "eur", Guid.NewGuid(), default));
        }
        else
        {
            Assert.Empty(await client.ListAsync(paymentId, default));
            Assert.StartsWith("re_local_", (await client.CreateAsync(paymentId, 2500, "eur", Guid.NewGuid(), default)).Id);
        }
    }

    private sealed class RefundEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Local";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Theory]
    [InlineData(2500, "eur", "EUR 25.00")]
    [InlineData(2500, "jpy", "JPY 2,500")]
    [InlineData(500, "isk", "ISK 5")]
    [InlineData(500, "ugx", "UGX 5")]
    [InlineData(1005, "huf", "HUF 10.05")]
    public void PaidAmountsUseTheProvidersCurrencyUnits(long amount, string currency, string expected)
        => Assert.Equal(expected, iD_Develops.Utilities.PaymentAmounts.Format(amount, currency, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefundWebhooksRequireSignaturesAndReconcileCurrentProviderState(bool validSignature)
    {
        await using var data = await Data.CreateAsync();
        data.Provider.Refunds.Add(new("re_webhook", data.Purchase.PaymentIntentId!, 2500, "eur", "succeeded"));
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = "evt_refund",
            @object = "event",
            type = "refund.updated",
            api_version = "2024-06-20",
            created = time,
            request = (object?)null,
            data = new
            {
                @object = new
                {
                    id = "re_webhook",
                    @object = "refund",
                    payment_intent = data.Purchase.PaymentIntentId,
                    amount = 2500,
                    currency = "eur",
                    status = "pending"
                }
            }
        });
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(validSignature ? "whsec_refund" : "wrong"));
        var hash = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{time}.{json}"))).ToLowerInvariant();
        var context = new DefaultHttpContext();
        context.Request.Headers["Stripe-Signature"] = $"t={time},v1={hash}";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var webhook = new StripeWebhookService(NullLogger<StripeWebhookService>.Instance,
            Options.Create(new StripeSettings { WebhookSecret = "whsec_refund" }), null!, null!, data.Db, null!, data.Service);
        var result = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(await webhook.CheckoutCompletedAsync(context.Request));
        Assert.Equal(validSignature ? 200 : 400, result.StatusCode ?? 200);
        Assert.Equal(validSignature ? "Refunded" : "None", (await data.ReloadAsync()).RefundStatus);
    }

    [Fact]
    public async Task FullUnusedRefundRemovesOnlyThatPurchasesBenefitsAndReplaysWithoutAnotherPayment()
    {
        await using var data = await Data.CreateAsync();
        var other = new UserCreditLot { UserId = data.User.Id, CreditTypeId = data.Lot.CreditTypeId, GrantedQuantity = 4, RemainingQuantity = 4, ExternalReference = "different-purchase" };
        data.Db.UserCreditLots.Add(other);
        await data.Db.SaveChangesAsync();
        var result = await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default);
        Assert.True(result.Accepted);
        Assert.Contains("processed", result.Message);
        Assert.Equal("Refunded", (await data.ReloadAsync()).RefundStatus);
        Assert.Equal(2500, (await data.ReloadAsync()).RefundedMinorUnits);
        Assert.True((await data.Db.PurchasedExamEntitlements.AsNoTracking().SingleAsync()).RefundBlocked);
        Assert.Equal(0, (await data.Db.UserCreditLots.AsNoTracking().SingleAsync(item => item.Id == data.Lot.Id)).RemainingQuantity);
        Assert.Equal(4, (await data.Db.UserCreditLots.AsNoTracking().SingleAsync(item => item.Id == other.Id)).RemainingQuantity);
        Assert.False((await data.Db.UserCreditLots.AsNoTracking().SingleAsync(item => item.Id == other.Id)).RefundBlocked);
        Assert.False((await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default)).Accepted);
        Assert.Single(await data.Db.CatalogRefunds.ToListAsync());
        Assert.Equal(1, data.Provider.Creates);
        Assert.Equal(AuditEventOutcome.Success, (await data.Db.ApplicationAuditEvents.SingleAsync(item => item.EventType == ApplicationAuditEventTypes.RefundUpdated)).Outcome);
        Assert.Single(await data.Db.UserCreditTransactions.Where(item => item.TransactionType == CreditTransactionType.Refund).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartedOrDeletedCancelledExamRecordsPreventRefunds(bool deleted)
    {
        await using var data = await Data.CreateAsync();
        data.Db.Records.Add(new()
        {
            UserId = data.User.Id,
            ExamId = data.Exam.Id,
            ExamVersionId = data.Grant.ExamVersionId,
            PurchasedExamEntitlementId = data.Grant.Id,
            StartDateTime = DateTime.UtcNow,
            ExamStatus = deleted ? ExamStatus.Cancelled : ExamStatus.InProgress,
            IsDeleted = deleted
        });
        await data.Db.SaveChangesAsync();
        var result = await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default);
        Assert.False(result.Accepted);
        Assert.Contains("has been used", result.Message);
        Assert.Equal(0, data.Provider.Creates);
        Assert.Empty(await data.Db.CatalogRefunds.ToListAsync());
    }

    [Fact]
    public async Task ReturnedCreditsStillCountAsUse()
    {
        await using var data = await Data.CreateAsync();
        data.Db.UserCreditTransactions.Add(new()
        {
            UserId = data.User.Id,
            CreditTypeId = data.Lot.CreditTypeId,
            UserCreditLotId = data.Lot.Id,
            TransactionType = CreditTransactionType.Return,
            QuantityDelta = 1
        });
        await data.Db.SaveChangesAsync();
        Assert.False((await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default)).Accepted);
        Assert.Equal(0, data.Provider.Creates);
    }

    [Fact]
    public async Task AnotherAccountCannotSeeOrRefundAPurchaseEvenWithTheSameEmail()
    {
        await using var data = await Data.CreateAsync();
        data.Db.Users.Add(new() { Id = "other", UserName = "other", Email = data.User.Email });
        await data.Db.SaveChangesAsync();
        Assert.Null(await data.Service.GetOwnedAsync(data.Purchase.Id, "other", default));
        Assert.False((await data.Service.RequestAsync(data.Purchase.Id, "other", default)).Accepted);
        Assert.Equal(0, data.Provider.Creates);
    }

    [Fact]
    public async Task PendingRefundBlocksAnAtomicExamStartAndReleasesBenefitsOnDefinitiveFailure()
    {
        await using var data = await Data.CreateAsync();
        data.Provider.NextStatus = "pending";
        Assert.True((await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default)).Accepted);
        Assert.Equal("Pending", (await data.ReloadAsync()).RefundStatus);
        var options = Options.Create(new RecordLifecycleOptions());
        var records = new RecordService(data.Db, NullLogger<RecordService>.Instance, options, new ExamVersionService(data.Db),
            new ParticipantAnswerService(data.Db, options), new ExamEvaluationService());
        Assert.Equal(Guid.Empty, await records.StartPurchasedAttemptAtomicAsync(data.User.Id, data.Exam.Id, data.Grant.Id, data.Grant.ExamVersionId, DateTime.UtcNow, null));
        Assert.False(await new ExamAccessService(data.Db).CanTakeExamAsync(data.User.Id, data.Exam.Id));
        data.Provider.Refunds[0] = data.Provider.Refunds[0] with { Status = "failed" };
        await data.Service.ReconcilePaymentAsync(data.Purchase.PaymentIntentId, default);
        Assert.Equal("Failed", (await data.ReloadAsync()).RefundStatus);
        Assert.False((await data.ReloadAsync()).RefundBlocked);
        Assert.True((await data.Service.EligibilityAsync(await data.ReloadAsync(), default)).CanRefund);
        Assert.True(await new ExamAccessService(data.Db).CanTakeExamAsync(data.User.Id, data.Exam.Id));
    }

    [Fact]
    public async Task LostCreateResponseIsReconciledWithoutIssuingADuplicateRefund()
    {
        await using var data = await Data.CreateAsync();
        data.Provider.LoseResponseOnce = true;
        Assert.True((await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default)).Accepted);
        Assert.Equal("Pending", (await data.ReloadAsync()).RefundStatus);
        Assert.True((await data.ReloadAsync()).RefundBlocked);
        var request = await data.Db.CatalogRefunds.AsNoTracking().SingleAsync();
        await data.Service.ProcessAsync(request.Id, default);
        Assert.Equal("Refunded", (await data.ReloadAsync()).RefundStatus);
        Assert.Equal(1, data.Provider.Creates);
        Assert.Single(data.Provider.Refunds);
        Assert.Single(await data.Db.CatalogRefunds.ToListAsync());
    }

    [Fact]
    public async Task ExternalRefundIsObservedIdempotentlyAndLateFailureRestoresOnlyRevokedCredits()
    {
        await using var data = await Data.CreateAsync();
        data.Provider.Refunds.Add(new("re_external", data.Purchase.PaymentIntentId!, 2500, "eur", "succeeded"));
        await data.Service.ReconcilePaymentAsync(data.Purchase.PaymentIntentId, default);
        await data.Service.ReconcilePaymentAsync(data.Purchase.PaymentIntentId, default);
        Assert.Equal("Refunded", (await data.ReloadAsync()).RefundStatus);
        Assert.Single(await data.Db.CatalogRefunds.ToListAsync());
        Assert.Single(await data.Db.UserCreditTransactions.Where(item => item.TransactionType == CreditTransactionType.Refund).ToListAsync());
        data.Provider.Refunds[0] = data.Provider.Refunds[0] with { Status = "failed" };
        await data.Service.ReconcilePaymentAsync(data.Purchase.PaymentIntentId, default);
        await data.Service.ReconcilePaymentAsync(data.Purchase.PaymentIntentId, default);
        var lot = await data.Db.UserCreditLots.AsNoTracking().SingleAsync();
        Assert.Equal(3, lot.RemainingQuantity);
        Assert.Equal(0, lot.RefundRevokedQuantity);
        Assert.False(lot.RefundBlocked);
        Assert.True((await data.Service.EligibilityAsync(await data.ReloadAsync(), default)).CanRefund);
        Assert.Single(await data.Db.UserCreditTransactions.Where(item => item.TransactionType == CreditTransactionType.RefundReversed).ToListAsync());
    }

    [Fact]
    public async Task PartialExternalRefundPreventsIssuingTheFullAmountAgain()
    {
        await using var data = await Data.CreateAsync();
        data.Provider.Refunds.Add(new("re_partial", data.Purchase.PaymentIntentId!, 500, "eur", "succeeded"));
        await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default);
        Assert.Equal("PartiallyRefunded", (await data.ReloadAsync()).RefundStatus);
        Assert.Equal(500, (await data.ReloadAsync()).RefundedMinorUnits);
        Assert.Equal(0, data.Provider.Creates);
        Assert.False((await data.Service.EligibilityAsync(await data.ReloadAsync(), default)).CanRefund);
    }

    [Theory]
    [InlineData("requires_action", "RequiresAction")]
    [InlineData("new_unknown_status", "ReviewRequired")]
    public async Task UnfinishedProviderStatesRemainOnHold(string providerStatus, string expected)
    {
        await using var data = await Data.CreateAsync();
        data.Provider.NextStatus = providerStatus;
        await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default);
        Assert.Equal(expected, (await data.ReloadAsync()).RefundStatus);
        Assert.True((await data.ReloadAsync()).RefundBlocked);
        Assert.Equal(0, (await data.ReloadAsync()).RefundedMinorUnits);
    }

    [Fact]
    public async Task UnknownOutcomesOlderThanTheIdempotencyWindowRequireCompanyReview()
    {
        await using var data = await Data.CreateAsync();
        var request = new CatalogRefund { CatalogPurchaseId = data.Purchase.Id, AmountMinorUnits = 2500, CreatedAtUtc = DateTime.UtcNow.AddDays(-2) };
        data.Purchase.RefundBlocked = true;
        data.Purchase.RefundStatus = "Pending";
        data.Db.CatalogRefunds.Add(request);
        await data.Db.SaveChangesAsync();
        await data.Service.ProcessAsync(request.Id, default);
        Assert.Equal(0, data.Provider.Creates);
        Assert.Equal("ReviewRequired", (await data.ReloadAsync()).RefundStatus);
    }

    [Fact]
    public async Task UnknownBenefitsAndMissingEvidenceRequireManualReview()
    {
        await using var data = await Data.CreateAsync();
        data.Purchase.HasUntrackedBenefits = true;
        await data.Db.SaveChangesAsync();
        Assert.False((await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default)).Accepted);
        await data.Db.CatalogPurchases.Where(item => item.Id == data.Purchase.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.HasUntrackedBenefits, false));
        await data.Db.PurchasedExamEntitlements.ExecuteDeleteAsync();
        Assert.False((await data.Service.RequestAsync(data.Purchase.Id, data.User.Id, default)).Accepted);
        Assert.Equal(0, data.Provider.Creates);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ExternalRefundPreservesIndependentCourseAccessAndOtherPaidPurchases(bool manual, bool otherPurchase)
    {
        await using var data = await Data.CreateAsync();
        var course = new Course { Name = "Purchased course" };
        data.Db.Courses.Add(course);
        await data.Db.SaveChangesAsync();
        var grantedAt = DateTime.UtcNow;
        data.Purchase.GrantedCourseId = course.Id;
        data.Purchase.OwnedCourseAccessGrantedAtUtc = manual ? null : grantedAt;
        data.Purchase.HasUntrackedBenefits = true;
        data.Db.UserCourses.Add(new()
        {
            UserId = data.User.Id,
            CourseId = course.Id,
            GrantedAtUtc = grantedAt,
            PurchasedAtUtc = manual ? null : grantedAt,
            AssignmentSource = manual ? CourseAssignmentSource.Admin : CourseAssignmentSource.Purchase
        });
        if (otherPurchase) data.Db.CatalogPurchases.Add(new()
        {
            UserId = data.User.Id,
            ProductName = "Other purchase",
            StripeSessionId = "cs_other",
            PaymentIntentId = "pi_other",
            GrantedCourseId = course.Id,
            FulfilledAtUtc = grantedAt
        });
        await data.Db.SaveChangesAsync();
        if (manual)
            Assert.False((await new CourseService(data.Db).RemoveUserAsync(course.Id, data.User.Id, "manager", true)).Success);
        data.Provider.Refunds.Add(new("re_external", data.Purchase.PaymentIntentId!, 2500, "eur", "succeeded"));
        await data.Service.ReconcilePaymentAsync(data.Purchase.PaymentIntentId, default);
        Assert.Equal(manual || otherPurchase, await data.Db.UserCourses.AnyAsync());
        if (manual)
            Assert.True((await new CourseService(data.Db).RemoveUserAsync(course.Id, data.User.Id, "manager", true)).Success);
        if (!manual && !otherPurchase)
        {
            data.Provider.Refunds[0] = data.Provider.Refunds[0] with { Status = "failed" };
            await data.Service.ReconcilePaymentAsync(data.Purchase.PaymentIntentId, default);
            Assert.Equal(grantedAt, (await data.Db.UserCourses.AsNoTracking().SingleAsync()).PurchasedAtUtc);
        }
    }

    [Fact]
    public async Task PurchaseLedgerRetainsTheCheckoutNameAndAccountAfterTemporaryDataAndProductAreDeleted()
    {
        await using var data = await Data.CreateAsync();
        var product = new CatalogProduct { Name = "Edited name", Slug = "edited-name" };
        data.Db.CatalogProducts.Add(product);
        await data.Db.SaveChangesAsync();
        var attempt = new CatalogCheckoutAttempt
        {
            CatalogProductId = product.Id,
            ProductNameSnapshot = "Original purchased name",
            AuthenticatedUserId = data.User.Id,
            StripeSessionId = "cs_ledger",
            AmountTotalMinorUnits = 2700,
            Currency = "eur",
            Quantity = 1
        };
        data.Db.CatalogCheckoutAttempts.Add(attempt);
        await data.Db.SaveChangesAsync();
        await CatalogPurchaseLedger.RecordPaymentAsync(data.Db, new Stripe.Checkout.Session
        {
            Id = "cs_ledger",
            Status = "complete",
            PaymentStatus = "paid",
            AmountTotal = 2700,
            Currency = "eur",
            PaymentIntentId = "pi_ledger",
            Metadata = new() { ["checkout_attempt_id"] = attempt.Id.ToString(), ["catalog_product_id"] = product.Id.ToString() }
        }, default);
        await data.Db.CatalogCheckoutAttempts.Where(item => item.Id == attempt.Id).ExecuteDeleteAsync();
        await data.Db.CatalogProducts.Where(item => item.Id == product.Id).ExecuteDeleteAsync();
        var saved = await data.Db.CatalogPurchases.AsNoTracking().SingleAsync(item => item.StripeSessionId == "cs_ledger");
        Assert.Equal("Original purchased name", saved.ProductName);
        Assert.Equal(2700, saved.AmountMinorUnits);
        Assert.Equal(data.User.Id, saved.UserId);
        Assert.Null(saved.CatalogProductId);
        Assert.Null(saved.FulfilledAtUtc);
    }

    [Fact]
    public async Task GuestPaymentsAreNotAttachedToAnAccountByEmail()
    {
        await using var data = await Data.CreateAsync();
        await CatalogPurchaseLedger.RecordPaymentAsync(data.Db, new Stripe.Checkout.Session
        {
            Id = "cs_guest",
            Status = "complete",
            PaymentStatus = "paid",
            AmountTotal = 2700,
            Currency = "eur",
            PaymentIntentId = "pi_guest",
            Metadata = new() { ["email"] = data.User.Email! }
        }, default);
        Assert.Null((await data.Db.CatalogPurchases.AsNoTracking().SingleAsync(item => item.StripeSessionId == "cs_guest")).UserId);
        var guest = await data.Db.CatalogPurchases.AsNoTracking().SingleAsync(item => item.StripeSessionId == "cs_guest");
        Assert.Null(await data.Service.GetOwnedAsync(guest.Id, null!, default));
        Assert.Null(await data.Service.GetOwnedAsync(guest.Id, string.Empty, default));
    }

    [Fact]
    public async Task AVerifiedAccountPaymentSurvivesMissingTemporaryCheckoutData()
    {
        await using var data = await Data.CreateAsync();
        var reference = Guid.NewGuid();
        await CatalogPurchaseLedger.RecordPaymentAsync(data.Db, new Stripe.Checkout.Session
        {
            Id = "cs_missing_checkout",
            Status = "complete",
            PaymentStatus = "paid",
            AmountTotal = 2700,
            Currency = "eur",
            PaymentIntentId = "pi_missing_checkout",
            Metadata = new()
            {
                ["authenticated_user_id"] = data.User.Id,
                ["checkout_attempt_id"] = reference.ToString(),
                ["product_name_snapshot"] = "Original product"
            }
        }, default);
        var saved = await data.Db.CatalogPurchases.AsNoTracking().SingleAsync(item => item.StripeSessionId == "cs_missing_checkout");
        Assert.Equal(data.User.Id, saved.UserId);
        Assert.Equal(reference, saved.CheckoutAttemptId);
        Assert.Equal("Original product", saved.ProductName);
        Assert.Null(saved.FulfilledAtUtc);
        Assert.False((await data.Service.EligibilityAsync(saved, default)).CanRefund);
    }

    private sealed class Provider : IStripeRefundClient
    {
        public bool IsAvailable => true;
        public int Creates { get; private set; }
        public string NextStatus { get; set; } = "succeeded";
        public bool LoseResponseOnce { get; set; }
        public List<ProviderRefund> Refunds { get; } = new();
        public Task<IReadOnlyList<ProviderRefund>> ListAsync(string paymentIntentId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProviderRefund>>(Refunds.Where(item => item.PaymentIntentId == paymentIntentId).ToArray());
        public Task<ProviderRefund> CreateAsync(string paymentIntentId, long amount, string currency, Guid requestId, CancellationToken ct)
        {
            Creates++;
            var result = Refunds.FirstOrDefault(item => item.RequestId == requestId.ToString());
            if (result == null)
            {
                result = new("re_" + requestId.ToString("N"), paymentIntentId, amount, currency, NextStatus, requestId.ToString());
                Refunds.Add(result);
            }
            if (LoseResponseOnce) { LoseResponseOnce = false; throw new HttpRequestException("The provider accepted the request, but its response was lost."); }
            return Task.FromResult(result);
        }
    }

    private sealed class Data(SqliteTestDbFactory factory, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public ApplicationUser User { get; private set; } = null!;
        public Exam Exam { get; private set; } = null!;
        public PurchasedExamEntitlement Grant { get; private set; } = null!;
        public UserCreditLot Lot { get; private set; } = null!;
        public CatalogPurchase Purchase { get; private set; } = null!;
        public Provider Provider { get; } = new();
        public CatalogRefundService Service => new(Db, Provider, new ApplicationAuditService(Db, new HttpContextAccessor(), NullLogger<ApplicationAuditService>.Instance), NullLogger<CatalogRefundService>.Instance);
        public Task<CatalogPurchase> ReloadAsync() => Db.CatalogPurchases.AsNoTracking().SingleAsync(item => item.Id == Purchase.Id);

        public static async Task<Data> CreateAsync()
        {
            var factory = new SqliteTestDbFactory();
            var db = factory.CreateDbContext();
            var data = new Data(factory, db);
            data.User = new() { Id = "buyer", UserName = "buyer", Email = "buyer@example.test" };
            data.Exam = new()
            {
                Name = "Paid exam",
                CreatedByUserId = "teacher",
                IntroductionPrimaryLanguage = "Exam instructions",
                PublishStatus = ExamPublishStatus.Published,
                MaxAttempts = 1,
                TimeLimitSeconds = 120,
                Questions = [new OpenQuestion { Text = "Question", QuestionNumber = 1 }]
            };
            var credit = new CreditType { Name = "Lesson", NormalizedName = "LESSON", SingularLabel = "lesson", PluralLabel = "lessons" };
            db.AddRange(data.User, data.Exam, credit);
            await db.SaveChangesAsync();
            var version = await new ExamVersionService(db).GetOrCreateAttemptVersionAsync(data.Exam.Id);
            data.Grant = new() { UserId = data.User.Id, ExamId = data.Exam.Id, ExamVersionId = version!.ExamVersionId!.Value, TotalAttempts = 2, StripeSessionId = "cs_purchase" };
            data.Lot = new() { UserId = data.User.Id, CreditTypeId = credit.Id, GrantedQuantity = 3, RemainingQuantity = 3, ExternalReference = "cs_purchase" };
            data.Purchase = new()
            {
                UserId = data.User.Id,
                StripeSessionId = "cs_purchase",
                PaymentIntentId = "pi_purchase",
                ProductName = "Exam and credits",
                AmountMinorUnits = 2500,
                Currency = "eur",
                FulfilledAtUtc = DateTime.UtcNow,
                HasUntrackedBenefits = false,
                ExamEntitlementCount = 1,
                CreditLotCount = 1
            };
            db.AddRange(data.Grant, data.Lot, data.Purchase);
            await db.SaveChangesAsync();
            return data;
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); factory.Dispose(); }
    }
}
