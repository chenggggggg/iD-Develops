using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PurchaseRefundTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task UnusedPurchaseCanBeConfirmedAndRefundedWithoutAffectingAnotherPurchase()
    {
        await using var data = await RefundFixture.CreateAsync(twoPurchases: true);
        await LoginAsync(data.Account, "/profile");
        var first = Page.Locator($"[data-purchase-id='{data.Purchase.Id}']");
        await first.GetByRole(AriaRole.Link, new() { Name = "Request refund", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Request a refund", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("EUR 25.00", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("This is a local test purchase. No money will move.", new() { Exact = true })).ToBeVisibleAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "refund-confirm-desktop.png") });
        await Page.SetViewportSizeAsync(390, 844);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "refund-confirm-mobile.png") });
        Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm refund", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Your refund has been processed.");
        await Expect(first.Locator("[data-purchase-status]")).ToHaveTextAsync("Refunded");
        await Expect(first.GetByRole(AriaRole.Link, new() { Name = "Request refund", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.Locator($"[data-purchase-id='{data.OtherPurchase!.Id}']").GetByRole(AriaRole.Link, new() { Name = "Request refund", Exact = true })).ToBeVisibleAsync();
        await Page.ReloadAsync();
        await Expect(first.Locator("[data-purchase-status]")).ToHaveTextAsync("Refunded");
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.Single(await db.CatalogRefunds.Where(item => item.CatalogPurchaseId == data.Purchase.Id).ToListAsync());
        Assert.True((await db.PurchasedExamEntitlements.SingleAsync(item => item.StripeSessionId == data.Purchase.StripeSessionId)).RefundBlocked);
        Assert.False((await db.PurchasedExamEntitlements.SingleAsync(item => item.StripeSessionId == data.OtherPurchase.StripeSessionId)).RefundBlocked);
        Assert.Equal(3, await db.UserCreditLots.Where(item => item.UserId == data.Account.User.Id && !item.RefundBlocked).SumAsync(item => item.RemainingQuantity));
        Assert.True(await db.ApplicationAuditEvents.AnyAsync(item => item.EventType == ApplicationAuditEventTypes.RefundUpdated && item.EntityId == data.Purchase.Id.ToString()));
        await Page.SetViewportSizeAsync(1440, 1000);
        await Page.Locator("#purchase-history").ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "purchase-history-desktop.png") });
        await Page.SetViewportSizeAsync(390, 844);
        await Page.Locator("#purchase-history").ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "purchase-history-mobile.png") });
        Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }

    [Fact]
    public async Task PurchaseHistoryIsBoundedAndOlderPurchasesRemainAccessible()
    {
        await using var data = await RefundFixture.CreateAsync();
        await using (var db = LocalTestApplication.CreateDbContext())
        {
            db.CatalogPurchases.AddRange(Enumerable.Range(1, 25).Select(index => new CatalogPurchase
            {
                UserId = data.Account.User.Id,
                ProductName = "Older purchase " + index,
                StripeSessionId = "local_history_" + Guid.NewGuid().ToString("N"),
                AmountMinorUnits = 100,
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-index),
                FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-index),
                IsLocal = true
            }));
            await db.SaveChangesAsync();
        }
        await LoginAsync(data.Account, "/profile");
        await Expect(Page.Locator("[data-purchase-id]")).ToHaveCountAsync(20);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Older purchases", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-purchase-id]")).ToHaveCountAsync(6);
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Purchase history pages", Exact = true })).ToContainTextAsync("Page 2 of 2");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Previous purchases", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-purchase-id]")).ToHaveCountAsync(20);
    }

    [Fact]
    public async Task UsedPurchaseShowsCompanyContactAndForgedPostCannotRefundIt()
    {
        await using var data = await RefundFixture.CreateAsync();
        await using (var db = LocalTestApplication.CreateDbContext())
        {
            var grant = await db.PurchasedExamEntitlements.SingleAsync(item => item.StripeSessionId == data.Purchase.StripeSessionId);
            grant.UsedAttempts = 1;
            db.Records.Add(new()
            {
                UserId = data.Account.User.Id,
                ExamId = data.ExamId,
                ExamVersionId = grant.ExamVersionId,
                PurchasedExamEntitlementId = grant.Id,
                StartDateTime = DateTime.UtcNow,
                ExamStatus = ExamStatus.Cancelled,
                IsDeleted = true
            });
            await db.SaveChangesAsync();
        }
        await LoginAsync(data.Account, "/profile");
        var row = Page.Locator($"[data-purchase-id='{data.Purchase.Id}']");
        await Expect(row).ToContainTextAsync("This purchase has been used.");
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = "Contact the company", Exact = true })).ToBeVisibleAsync();
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = "Request refund", Exact = true })).ToHaveCountAsync(0);
        Assert.Equal(200, await PostRefundAsync(data.Purchase.Id, includeToken: true));
        await using var check = LocalTestApplication.CreateDbContext();
        Assert.False(await check.CatalogRefunds.AnyAsync(item => item.CatalogPurchaseId == data.Purchase.Id));
        Assert.Equal(0, (await check.CatalogPurchases.SingleAsync(item => item.Id == data.Purchase.Id)).RefundedMinorUnits);
    }

    [Fact]
    public async Task AnotherAccountCannotReadOrPostToTheRefundConfirmation()
    {
        await using var data = await RefundFixture.CreateAsync();
        await using var other = await CatalogBrowsingFixture.CreateAsync();
        await LoginAsync(other, "/profile");
        await Expect(Page.Locator($"[data-purchase-id='{data.Purchase.Id}']")).ToHaveCountAsync(0);
        Assert.Equal(404, await PostRefundAsync(data.Purchase.Id, includeToken: true));
        var response = await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/profile/refund/{data.Purchase.Id}");
        Assert.Equal(404, response!.Status);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Confirm refund", Exact = true })).ToHaveCountAsync(0);
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.CatalogRefunds.AnyAsync(item => item.CatalogPurchaseId == data.Purchase.Id));
    }

    [Fact]
    public async Task RefundPostRequiresAnAntiforgeryToken()
    {
        await using var data = await RefundFixture.CreateAsync();
        await LoginAsync(data.Account, "/profile");
        Assert.Equal(400, await PostRefundAsync(data.Purchase.Id, includeToken: false));
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.CatalogRefunds.AnyAsync(item => item.CatalogPurchaseId == data.Purchase.Id));
    }

    [Fact]
    public async Task PendingRefundDoesNotOfferAnotherRefundOrAllowAnExamStart()
    {
        await using var data = await RefundFixture.CreateAsync();
        await using (var db = LocalTestApplication.CreateDbContext())
        {
            await db.CatalogPurchases.Where(item => item.Id == data.Purchase.Id).ExecuteUpdateAsync(update => update
                .SetProperty(item => item.RefundBlocked, true).SetProperty(item => item.RefundStatus, "Pending"));
            await db.PurchasedExamEntitlements.Where(item => item.StripeSessionId == data.Purchase.StripeSessionId)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.RefundBlocked, true));
            await db.UserCreditLots.Where(item => item.ExternalReference == data.Purchase.StripeSessionId)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.RefundBlocked, true));
            db.CatalogRefunds.Add(new()
            {
                CatalogPurchaseId = data.Purchase.Id,
                ProviderRefundId = "re_local_pending_" + data.Purchase.Id.ToString("N"),
                Status = "pending",
                AmountMinorUnits = 2500,
                NextCheckAtUtc = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }
        await LoginAsync(data.Account, "/profile");
        var row = Page.Locator($"[data-purchase-id='{data.Purchase.Id}']");
        await Expect(row.Locator("[data-purchase-status]")).ToHaveTextAsync("Refund pending");
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = "Request refund", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByText("You do not currently have any available credits.", new() { Exact = true })).ToBeVisibleAsync();
        var status = await Page.EvaluateAsync<int>("""
            async examId => {
                const token = document.querySelector('[name=__RequestVerificationToken]').value;
                const response = await fetch('/exams?handler=LaunchExam', { method: 'POST', body: new URLSearchParams({ examId, __RequestVerificationToken: token }) });
                return response.status;
            }
            """, data.ExamId.ToString());
        Assert.Equal(200, status);
        await using var check = LocalTestApplication.CreateDbContext();
        Assert.False(await check.Records.AnyAsync(item => item.ExamId == data.ExamId));
    }

    [Fact]
    public async Task ConcurrentExamStartAndRefundCannotBothConsumeAndRefundThePurchase()
    {
        await using var data = await RefundFixture.CreateAsync();
        await LoginAsync(data.Account, "/exams");
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = data.ExamName });
        await row.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Start exam", Exact = true })).ToBeVisibleAsync();
        var statuses = await Page.EvaluateAsync<int[]>("""
            async purchaseId => {
                const form = document.querySelector('[data-exam-launch-primary-form]');
                const body = new URLSearchParams(new FormData(form));
                const token = body.get('__RequestVerificationToken');
                const responses = await Promise.all([
                    fetch(form.action, { method: 'POST', body }),
                    fetch('/profile/refund/' + purchaseId, { method: 'POST', body: new URLSearchParams({ __RequestVerificationToken: token }) })
                ]);
                return responses.map(response => response.status);
            }
            """, data.Purchase.Id.ToString());
        Assert.All(statuses, status => Assert.Equal(200, status));
        await using var db = LocalTestApplication.CreateDbContext();
        var purchase = await db.CatalogPurchases.AsNoTracking().SingleAsync(item => item.Id == data.Purchase.Id);
        var records = await db.Records.Where(item => item.ExamId == data.ExamId).ToListAsync();
        if (purchase.RefundStatus == "Refunded")
        {
            Assert.Empty(records);
            Assert.Equal(2500, purchase.RefundedMinorUnits);
        }
        else
        {
            Assert.Single(records);
            Assert.Equal(0, purchase.RefundedMinorUnits);
            Assert.False(await db.CatalogRefunds.AnyAsync(item => item.CatalogPurchaseId == purchase.Id));
        }
    }

    private Task<int> PostRefundAsync(Guid purchaseId, bool includeToken) => Page.EvaluateAsync<int>("""
        async args => {
            const body = new URLSearchParams();
            if (args.token) body.set('__RequestVerificationToken', document.querySelector('[name=__RequestVerificationToken]').value);
            return (await fetch('/profile/refund/' + args.id, { method: 'POST', body })).status;
        }
        """, new { id = purchaseId.ToString(), token = includeToken });

    private async Task LoginAsync(CatalogBrowsingFixture account, string path)
    {
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await Page.GetByLabel("E-mail").FillAsync(account.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(account.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(LocalTestApplication.PortalBaseUrl + path);
    }

    private sealed class RefundFixture(CatalogBrowsingFixture account, int examId, string examName) : IAsyncDisposable
    {
        public CatalogBrowsingFixture Account { get; } = account;
        public int ExamId { get; } = examId;
        public string ExamName { get; } = examName;
        public CatalogPurchase Purchase { get; private set; } = null!;
        public CatalogPurchase? OtherPurchase { get; private set; }
        private int CreditTypeId { get; set; }

        public static async Task<RefundFixture> CreateAsync(bool twoPurchases = false)
        {
            var account = await CatalogBrowsingFixture.CreateAsync();
            await using var db = LocalTestApplication.CreateDbContext();
            var exam = new Exam
            {
                Name = "Refund exam " + Guid.NewGuid().ToString("N"),
                CreatedByUserId = "refund-fixture-teacher",
                IntroductionPrimaryLanguage = "Instructions",
                MaxAttempts = 1,
                TimeLimitSeconds = 120,
                PublishStatus = ExamPublishStatus.Published,
                Questions = [new OpenQuestion { Text = "Question", QuestionNumber = 1, Score = 1 }]
            };
            var credit = new CreditType { Name = "Refund lesson " + Guid.NewGuid().ToString("N"), NormalizedName = Guid.NewGuid().ToString("N"), SingularLabel = "lesson", PluralLabel = "lessons" };
            db.AddRange(exam, credit);
            await db.SaveChangesAsync();
            var fixture = new RefundFixture(account, exam.Id, exam.Name) { CreditTypeId = credit.Id };
            try
            {
                var version = await new ExamVersionService(db).GetOrCreateAttemptVersionAsync(exam.Id);
                fixture.Purchase = AddPurchase();
                if (twoPurchases) fixture.OtherPurchase = AddPurchase();
                await db.SaveChangesAsync();
                return fixture;

                CatalogPurchase AddPurchase()
                {
                    var session = "local_" + Guid.NewGuid().ToString("N");
                    var purchase = new CatalogPurchase
                    {
                        UserId = account.User.Id,
                        CatalogProductId = account.Standard.Id,
                        ProductName = "Exam and lesson credits",
                        StripeSessionId = session,
                        PaymentIntentId = "local_payment_" + session,
                        AmountMinorUnits = 2500,
                        Currency = "eur",
                        IsLocal = true,
                        FulfilledAtUtc = DateTime.UtcNow,
                        HasUntrackedBenefits = false,
                        ExamEntitlementCount = 1,
                        CreditLotCount = 1
                    };
                    db.CatalogPurchases.Add(purchase);
                    db.PurchasedExamEntitlements.Add(new() { UserId = account.User.Id, ExamId = exam.Id, ExamVersionId = version!.ExamVersionId!.Value, StripeSessionId = session, TotalAttempts = 2 });
                    db.UserCreditLots.Add(new() { UserId = account.User.Id, CreditTypeId = credit.Id, GrantedQuantity = 3, RemainingQuantity = 3, ExternalReference = session });
                    return purchase;
                }
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var db = LocalTestApplication.CreateDbContext();
                await db.ParticipantAnswers.Where(item => item.Record.ExamId == ExamId).ExecuteDeleteAsync();
                await db.Records.Where(item => item.ExamId == ExamId).ExecuteDeleteAsync();
                await db.PurchasedExamEntitlements.Where(item => item.ExamId == ExamId).ExecuteDeleteAsync();
                await db.UserCreditTransactions.Where(item => item.UserId == Account.User.Id).ExecuteDeleteAsync();
                await db.UserCreditLots.Where(item => item.UserId == Account.User.Id).ExecuteDeleteAsync();
                await db.ApplicationAuditEvents.Where(item => item.SubjectUserId == Account.User.Id || item.ActorUserId == Account.User.Id || item.EntityType == nameof(Exam) && item.EntityId == ExamId.ToString()).ExecuteDeleteAsync();
                await db.CreditTypes.Where(item => item.Id == CreditTypeId).ExecuteDeleteAsync();
                await db.ExamLogs.Where(item => item.ExamId == ExamId).ExecuteDeleteAsync();
                await db.Questions.Where(item => item.ExamId == ExamId).ExecuteDeleteAsync();
                await db.Exams.Where(item => item.Id == ExamId).ExecuteDeleteAsync();
            }
            finally { await Account.DisposeAsync(); }
        }
    }
}
