using System.Text.RegularExpressions;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PurchasedExamCheckoutTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task ProductEditor_DefaultsAttemptsFromExamAndPersistsOverride()
    {
        await using var data = await PurchaseFixture.CreateAsync(administrator: true, attached: false);
        await LoginAsync(data.Account, $"/products/{data.Account.Standard.Slug}/edit");
        await Expect(Page.GetByLabel("Attempts on purchase", new() { Exact = true })).ToBeHiddenAsync();
        await Page.GetByLabel("Attached exam", new() { Exact = true }).SelectOptionAsync(data.ExamId.ToString());
        await Expect(Page.GetByLabel("Attempts on purchase", new() { Exact = true })).ToHaveValueAsync("4");
        await Page.GetByLabel("Attempts on purchase", new() { Exact = true }).FillAsync("2");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Attempts on purchase", new() { Exact = true })).ToHaveValueAsync("2");
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Attached exam", new() { Exact = true })).ToHaveValueAsync(data.ExamId.ToString());
        await Expect(Page.GetByLabel("Attempts on purchase", new() { Exact = true })).ToHaveValueAsync("2");
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Account.Standard.Id);
        Assert.Equal(data.ExamId, product.GrantedExamId);
        Assert.Equal(2, product.ExamAttemptsOnPurchase);
        Assert.False(product.EnableQuantity);
        await Page.GotoAsync(data.PublicUrl);
        await Expect(Page.Locator("[data-purchased-exam-benefit]")).ToContainTextAsync("2 attempts per purchase");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CheckoutRequiresSignIn_ThenDeliversPinnedExamAndConsumesOnlyOneAttempt(bool mobile, bool concurrentStarts)
    {
        await using var data = await PurchaseFixture.CreateAsync();
        await Context.AddInitScriptAsync("window.__examLoadErrors = []; window.addEventListener('error', event => window.__examLoadErrors.push(event.message)); window.addEventListener('unhandledrejection', event => window.__examLoadErrors.push(String(event.reason)));");
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
        await Page.RouteAsync("https://challenges.cloudflare.com/**", route => route.AbortAsync());
        await Page.GotoAsync(data.PublicUrl);
        await Expect(Page.Locator("[data-purchased-exam-benefit]")).ToContainTextAsync(data.ExamName);
        await Page.GetByLabel("Your email", new() { Exact = true }).FillAsync(data.Account.User.Email!);
        await PublicProductSubmissionPacing.ReserveAsync();
        await Page.Locator("#product-submit-form button[type=submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/register[?]"));
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == data.Account.Standard.Id));
        Assert.False(await db.PurchasedExamEntitlements.AnyAsync(item => item.ExamId == data.ExamId));
        await Page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Page.GetByLabel("E-mail").FillAsync(data.Account.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(data.Account.Password);
        // The sign-in handoff resumes checkout through a second rate-limited public request.
        await PublicProductSubmissionPacing.ReserveAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/payment-result[?].*local=true"));
        var purchase = await db.PurchasedExamEntitlements.AsNoTracking().SingleAsync(item => item.ExamId == data.ExamId);
        Assert.Equal(data.Account.User.Id, purchase.UserId);
        Assert.Equal(2, purchase.TotalAttempts);
        Assert.Equal(0, purchase.UsedAttempts);
        // A later edit/archive must not replace the version paid for.
        await db.Exams.Where(item => item.Id == data.ExamId).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.Name, "Updated exam definition")
            .SetProperty(item => item.TimeLimitSeconds, 15)
            .SetProperty(item => item.MaxAttempts, 99)
            .SetProperty(item => item.PublishStatus, ExamPublishStatus.Archived));
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/exams");
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = data.ExamName });
        await Expect(row).ToContainTextAsync("Purchased version");
        var launchScriptState = await Page.EvaluateAsync<string>("() => JSON.stringify({ initializer: typeof initializeExamLaunchDialog, scripts: Array.from(document.scripts).filter(script => script.src.includes('exams-list')).map(script => ({type: script.type, src: script.getAttribute('src'), consent: script.dataset.cookieconsent})) })");
        Assert.True(await Page.EvaluateAsync<bool>("() => typeof initializeExamLaunchDialog === 'function'"), launchScriptState);
        Page.RequestFailed += (_, request) => { if (request.Url.Contains("handler=LaunchInfo")) Console.WriteLine("Launch-info request failed: " + request.Failure); };
        Page.PageError += (_, error) => Console.WriteLine("Exam page error: " + error);
        var launchResponseTask = Page.WaitForResponseAsync(response => response.Url.Contains("handler=LaunchInfo", StringComparison.Ordinal));
        await row.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
        IResponse launchResponse;
        try { launchResponse = await launchResponseTask; }
        catch (TimeoutException error)
        {
            var diagnostics = await Page.EvaluateAsync<string>("() => JSON.stringify({ready: document.readyState, errors: window.__examLoadErrors, loading: document.querySelector('[data-exam-launch-loading]')?.hidden, scripts: Array.from(document.scripts).filter(script => script.src.includes('exams-list')).map(script => ({type: script.type, consent: script.dataset.cookieconsent}))})");
            throw new Xunit.Sdk.XunitException(error.Message + "\n" + diagnostics);
        }
        Assert.True(launchResponse.Ok, "Launch info returned HTTP " + launchResponse.Status + ": " + await launchResponse.TextAsync());
        using var launchInfo = System.Text.Json.JsonDocument.Parse(await launchResponse.TextAsync());
        Assert.Equal(120, launchInfo.RootElement.GetProperty("timeLimitSeconds").GetInt32());
        await Expect(Page.Locator("[data-exam-launch-time]")).ToHaveTextAsync("00:02:00");
        if (concurrentStarts)
        {
            var starts = await Page.EvaluateAsync<string[]>("""
                async () => {
                    const form = document.querySelector('[data-exam-launch-primary-form]');
                    const body = new URLSearchParams(new FormData(form));
                    const responses = await Promise.all([0, 1].map(() => fetch(form.action, { method: 'POST', body })));
                    return responses.map(response => response.status + ' ' + response.url);
                }
                """);
            Assert.All(starts, result => Assert.StartsWith("200 " + LocalTestApplication.PortalBaseUrl + "/examination/", result));
            Assert.Equal(starts[0], starts[1]);
            await Page.GotoAsync(starts[0][4..]);
        }
        else
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Start exam", Exact = true }).ClickAsync();
        }
        await Expect(Page.Locator("#RecordId")).ToHaveAttributeAsync("value", new Regex("^[0-9a-f-]{36}$"));
        await Page.ReloadAsync();
        var record = await db.Records.AsNoTracking().SingleAsync(item => item.ExamId == data.ExamId);
        Assert.Equal(purchase.Id, record.PurchasedExamEntitlementId);
        Assert.Equal(purchase.ExamVersionId, record.ExamVersionId);
        Assert.Equal(120, (record.EndDateTime!.Value - record.StartDateTime).TotalSeconds);
        Assert.Equal(1, (await db.PurchasedExamEntitlements.AsNoTracking().SingleAsync(item => item.Id == purchase.Id)).UsedAttempts);
        Assert.Single(await db.CatalogCheckoutReceipts.Where(item => item.StripeSessionId == purchase.StripeSessionId).ToListAsync());
    }

    [Fact]
    public async Task StudentCannotEditProductExamSettings_AndResultPageDoesNotGrantAccess()
    {
        await using var data = await PurchaseFixture.CreateAsync();
        await LoginAsync(data.Account, "/exams");
        var response = await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/products/{data.Account.Standard.Slug}/edit");
        Assert.True(response?.Status == 403 || Page.Url.Contains("/access-denied", StringComparison.Ordinal));
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/payment-result?local=true&checkoutProductId={data.Account.Standard.Id}");
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.PurchasedExamEntitlements.AnyAsync(item => item.ExamId == data.ExamId));
    }

    private async Task LoginAsync(CatalogBrowsingFixture account, string path)
    {
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await Page.GetByLabel("E-mail").FillAsync(account.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(account.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
    }

    private sealed class PurchaseFixture(CatalogBrowsingFixture account, int examId, string examName) : IAsyncDisposable
    {
        public CatalogBrowsingFixture Account { get; } = account;
        public int ExamId { get; } = examId;
        public string ExamName { get; } = examName;
        public string PublicUrl => $"{LocalTestApplication.PublicBaseUrl}/en-us/products/{Account.Standard.Slug}";
        public static async Task<PurchaseFixture> CreateAsync(bool administrator = false, bool attached = true)
        {
            var account = await CatalogBrowsingFixture.CreateAsync(administrator);
            await using var db = LocalTestApplication.CreateDbContext();
            var exam = new Exam
            {
                Name = $"Purchased exam {Guid.NewGuid():N}",
                CreatedByUserId = account.User.Id,
                IntroductionPrimaryLanguage = "Original purchased instructions",
                TimeLimitSeconds = 120,
                MaxAttempts = 4,
                PublishStatus = ExamPublishStatus.Published,
                Questions = [new OpenQuestion { Text = "Original paid question", QuestionNumber = 1, Score = 1 }]
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();
            var fixture = new PurchaseFixture(account, exam.Id, exam.Name);
            try
            {
                var product = await db.CatalogProducts.SingleAsync(item => item.Id == account.Standard.Id);
                product.WorkflowType = CatalogWorkflowType.FormThenStripeCheckout;
                product.GrantedExamId = attached ? exam.Id : null;
                product.ExamAttemptsOnPurchase = attached ? 2 : null;
                product.Variants = [new CatalogProductVariant { Name = "Standard", Price = 25, Currency = "EUR", IsActive = true, IsDefault = true }];
                product.FormFields = [new CatalogProductFormField { Key = "email", Label = "Your email", FieldType = CatalogFormFieldType.Email, IsRequired = true }];
                await db.SaveChangesAsync();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var db = LocalTestApplication.CreateDbContext();
                var sessions = await db.CatalogCheckoutAttempts.Where(item => item.CatalogProductId == Account.Standard.Id).Select(item => item.StripeSessionId).ToListAsync();
                await db.ParticipantAnswers.Where(item => item.Record.ExamId == ExamId).ExecuteDeleteAsync();
                await db.Records.Where(item => item.ExamId == ExamId).ExecuteDeleteAsync();
                await db.PurchasedExamEntitlements.Where(item => item.ExamId == ExamId).ExecuteDeleteAsync();
                await db.CatalogCheckoutMails.Where(item => sessions.Contains(item.StripeSessionId)).ExecuteDeleteAsync();
                await db.CatalogCheckoutReceipts.Where(item => sessions.Contains(item.StripeSessionId)).ExecuteDeleteAsync();
                await db.CatalogCheckoutAttempts.Where(item => item.CatalogProductId == Account.Standard.Id).ExecuteDeleteAsync();
                await db.CatalogProducts.Where(item => item.Id == Account.Standard.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.GrantedExamId, (int?)null));
                await db.ProductFormSubmissions.Where(item => item.FormName == Account.Standard.Name).ExecuteDeleteAsync();
                await db.ApplicationAuditEvents.Where(item => item.SubjectUserId == Account.User.Id || item.EntityType == nameof(Exam) && item.EntityId == ExamId.ToString()).ExecuteDeleteAsync();
                await db.ExamLogs.Where(item => item.ExamId == ExamId).ExecuteDeleteAsync();
                await db.Questions.Where(item => item.ExamId == ExamId).ExecuteDeleteAsync();
                await db.Exams.Where(item => item.Id == ExamId).ExecuteDeleteAsync();
            }
            finally { await Account.DisposeAsync(); }
        }
    }
}
