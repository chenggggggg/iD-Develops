using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class SignupTurnstileTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task SignupReturnsSuccessAndDeliversBothQueuedNotifications()
    {
        _ = FileMailDirectory();
        await using var data = await SignupAsync();
        try
        {
            await OpenAsync(data);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
            await Page.GetByLabel("Phone number", new() { Exact = false }).FillAsync("+31 20 123 4567");
            await Page.GetByLabel("Street address", new() { Exact = false }).FillAsync("Example Street 10");
            await Page.GetByLabel("Postal code", new() { Exact = false }).FillAsync("1234 AB");
            await Page.GetByLabel("City", new() { Exact = false }).FillAsync("Amsterdam");
            await Page.GetByLabel("Country", new() { Exact = false }).FillAsync("Netherlands");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await PublicProductSubmissionPacing.ReserveAsync();
            await Page.RunAndWaitForResponseAsync(() => Page.Locator("#product-submit-form button[type=submit]").ClickAsync(), IsSignupPost);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You're signed up!", Exact = true })).ToBeVisibleAsync();
            await AssertSavedOnceAsync(data);
            await AssertQueuedDeliveryAsync(data);
            await Page.ReloadAsync();
            await Expect(Page.Locator("[data-signup-confirmation]")).ToBeVisibleAsync();
            await Expect(Page.Locator("#product-submit-form")).ToHaveCountAsync(0);
            await AssertSavedOnceAsync(data);
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task FirstClickWaitsForChallengeWithSpinnerAndSendsOnlyOnePost()
    {
        await using var data = await SignupAsync();
        try
        {
            await OpenAsync(data);
            await InstallWidgetAsync();
            var posts = 0;
            Page.Request += (_, request) => { if (request.Method == "POST" && request.Url.Contains(data.Standard.Slug)) posts++; };
            var submit = Page.Locator("#product-submit-form button[type=submit]");
            await submit.ClickAsync();
            await Expect(Page.Locator("#product-submit-form")).ToHaveAttributeAsync("aria-busy", "true");
            await Expect(submit.Locator("[data-turnstile-spinner]")).ToBeVisibleAsync();
            await submit.ClickAsync();
            Assert.Equal(0, posts);
            await PublicProductSubmissionPacing.ReserveAsync();
            await Page.RunAndWaitForResponseAsync(() => Page.EvaluateAsync("() => window.testChallenge.callback('fresh-token')"), IsSignupPost);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You're signed up!", Exact = true })).ToBeVisibleAsync();
            Assert.Equal(1, posts);
            await AssertSavedOnceAsync(data);
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task ExpiredChallengeIsClearedAndRefreshedBeforeSubmitting()
    {
        await using var data = await SignupAsync();
        try
        {
            await OpenAsync(data);
            await InstallWidgetAsync();
            await Page.EvaluateAsync("() => { window.testChallenge.callback('old-token'); window.testChallenge['expired-callback'](); }");
            await Expect(Page.Locator("[data-turnstile-token]")).ToHaveValueAsync("");
            await Expect(Page.GetByText("Security check expired.", new() { Exact = true })).ToBeVisibleAsync();
            await Page.Locator("#product-submit-form button[type=submit]").ClickAsync();
            Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.challengeResets"));
            await PublicProductSubmissionPacing.ReserveAsync();
            var response = await Page.RunAndWaitForResponseAsync(() => Page.EvaluateAsync("() => window.testChallenge.callback('renewed-token')"), IsSignupPost);
            Assert.Contains("cf-turnstile-response=renewed-token", response.Request.PostData!);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You're signed up!", Exact = true })).ToBeVisibleAsync();
            await AssertSavedOnceAsync(data);
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task BlockedCloudflareShowsInlineRecoveryAndNeverPostsAnUnverifiedForm()
    {
        await using var data = await SignupAsync();
        try
        {
            await Page.RouteAsync("https://challenges.cloudflare.com/**", route => route.AbortAsync());
            await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
            await OpenAsync(data);
            await InstallWidgetAsync(blockApi: true);
            await Expect(Page.Locator("[data-turnstile-retry]")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Security check unavailable.", new() { Exact = true })).ToBeVisibleAsync();
            await Page.Locator("#product-submit-form button[type=submit]").ClickAsync();
            await Expect(Page.GetByText("Security check unavailable.", new() { Exact = true })).ToBeVisibleAsync();
            await using var db = LocalTestApplication.CreateDbContext();
            Assert.False(await db.ProductFormSubmissions.AnyAsync(row => row.FormName == data.Standard.Name));
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task ConcurrentSignupPostsSaveOneSubmissionAndOneNotificationPair()
    {
        await using var data = await SignupAsync();
        try
        {
            await OpenAsync(data);
            await PublicProductSubmissionPacing.ReserveAsync(2);
            var statuses = await Page.EvaluateAsync<int[]>("""
                async () => {
                    const body = new URLSearchParams(new FormData(document.getElementById('product-submit-form')));
                    const results = await Promise.all([0, 1].map(() => fetch(location.href, { method: 'POST', body })));
                    return results.map(response => response.status);
                }
                """);
            Assert.All(statuses, status => Assert.Equal(200, status));
            await AssertSavedOnceAsync(data);
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task StepsRequirePersonalDetailsPreserveAnswersAndNeverSaveBeforeFinalSubmit()
    {
        await using var data = await SignupAsync();
        try
        {
            await Page.SetViewportSizeAsync(1280, 900);
            await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{data.Standard.Slug}?signupReceipt=forged");
            await Expect(Page.Locator("[data-signup-confirmation]")).ToHaveCountAsync(0);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "signup-product-desktop.png"), FullPage = true });
            await Page.SetViewportSizeAsync(390, 844);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Sign up", Exact = true }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Expect(Page.Locator("[data-signup-step='1']")).ToBeVisibleAsync();
            await Expect(Page.GetByLabel("Full name", new() { Exact = true })).ToBeFocusedAsync();
            await Page.GetByLabel("Full name", new() { Exact = true }).FillAsync("Signup customer");
            await Page.GetByLabel("Email address", new() { Exact = true }).FillAsync("invalid");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Expect(Page.GetByLabel("Email address", new() { Exact = true })).ToBeFocusedAsync();
            await Page.GetByLabel("Email address", new() { Exact = true }).FillAsync(data.User.Email!);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "signup-questions-mobile.png"), FullPage = true });
            await Expect(Page.GetByLabel("Your email", new() { Exact = true })).ToHaveCountAsync(0);
            await Page.GetByLabel("Learning goals", new() { Exact = true }).FillAsync("Keep this answer");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
            await Expect(Page.GetByLabel("Full name", new() { Exact = true })).ToHaveValueAsync("Signup customer");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Expect(Page.GetByLabel("Learning goals", new() { Exact = true })).ToHaveValueAsync("Keep this answer");
            Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));
            await using var db = LocalTestApplication.CreateDbContext();
            Assert.False(await db.ProductFormSubmissions.AnyAsync(row => row.FormName == data.Standard.Name));
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task ServerValidationRejectsMissingPersonalDetailsAndReturnsToTheirStep()
    {
        await using var data = await SignupAsync();
        try
        {
            await OpenAsync(data);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
            await Page.GetByLabel("Email address", new() { Exact = true }).FillAsync("");
            await PublicProductSubmissionPacing.ReserveAsync();
            await Page.RunAndWaitForNavigationAsync(() => Page.EvaluateAsync("() => HTMLFormElement.prototype.submit.call(document.getElementById('product-submit-form'))"));
            await Expect(Page.Locator("[data-signup-step='1']")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-product-errors]")).ToContainTextAsync("Enter a valid email address.");
            await Expect(Page.GetByLabel("Full name", new() { Exact = true })).ToHaveValueAsync("Signup customer");
            await using var db = LocalTestApplication.CreateDbContext();
            Assert.False(await db.ProductFormSubmissions.AnyAsync(row => row.FormName == data.Standard.Name));
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task PersonalDetailsOnlySignupStillQueuesBothEmails()
    {
        await using var data = await SignupAsync();
        try
        {
            await using (var db = LocalTestApplication.CreateDbContext())
            {
                await db.CatalogProductFormFields.Where(field => field.CatalogProductId == data.Standard.Id).ExecuteDeleteAsync();
            }
            await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{data.Standard.Slug}");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Sign up", Exact = true }).ClickAsync();
            await Page.GetByLabel("Full name", new() { Exact = true }).FillAsync("Signup customer");
            await Page.GetByLabel("Email address", new() { Exact = true }).FillAsync(data.User.Email!);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("No additional questions. You can complete your signup now.")).ToBeVisibleAsync();
            await PublicProductSubmissionPacing.ReserveAsync();
            await Page.RunAndWaitForResponseAsync(() => Page.Locator("#product-submit-form button[type=submit]").ClickAsync(), IsSignupPost);
            await Expect(Page.Locator("[data-signup-confirmation]")).ToBeVisibleAsync();
            await AssertSavedOnceAsync(data);
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task InvalidCustomAnswersReturnToQuestionStepWithPersonalDetailsPreserved()
    {
        await using var data = await SignupAsync();
        try
        {
            await OpenAsync(data);
            await Page.GetByLabel("Learning goals", new() { Exact = true }).FillAsync("");
            await PublicProductSubmissionPacing.ReserveAsync();
            await Page.RunAndWaitForNavigationAsync(() => Page.EvaluateAsync("() => HTMLFormElement.prototype.submit.call(document.getElementById('product-submit-form'))"));
            await Expect(Page.Locator("[data-signup-step='2']")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-product-errors]")).ToContainTextAsync("Learning goals");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
            await Expect(Page.GetByLabel("Email address", new() { Exact = true })).ToHaveValueAsync(data.User.Email!);
            await using var db = LocalTestApplication.CreateDbContext();
            Assert.False(await db.ProductFormSubmissions.AnyAsync(row => row.FormName == data.Standard.Name));
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task EditorSeparatesFixedPersonalDetailsAndKeepsLegacyFieldsWhenSaving()
    {
        await using var data = await SignupAsync(administrator: true);
        try
        {
            var path = $"/products/{data.Standard.Slug}/edit";
            await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
            await Page.GetByLabel("E-mail").FillAsync(data.User.Email!);
            await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(data.Password);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
            await Page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
            await Expect(Page.GetByText("Full name and email address are always required.", new() { Exact = false })).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-form-field-item]")).ToHaveCountAsync(1);
            await Expect(Page.Locator("#EditInput_FormFields_0__Label")).ToHaveValueAsync("Learning goals");
            await Page.RunAndWaitForResponseAsync(() => Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync(),
                response => response.Request.Method == "POST" && response.Url.Contains("handler=SaveContent"));
            await Expect(Page.GetByText("Product published.", new() { Exact = true })).ToBeVisibleAsync();
            await using var db = LocalTestApplication.CreateDbContext();
            var fields = await db.CatalogProductFormFields.Where(field => field.CatalogProductId == data.Standard.Id).ToListAsync();
            Assert.Equal(5, fields.Count);
            Assert.Contains(fields, field => field.Key == "name");
            Assert.Contains(fields, field => field.Key == "email");
            Assert.Contains(fields, field => field.Key == "phone");
            Assert.Contains(fields, field => field.Key == "address");
            await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}{path}?preview=true");
            await Expect(Page.Locator("[data-signup-step='0']")).ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Sign up", Exact = true }).ClickAsync();
            await Page.GetByLabel("Full name", new() { Exact = true }).FillAsync("Preview visitor");
            await Page.GetByLabel("Email address", new() { Exact = true }).FillAsync(data.User.Email!);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Expect(Page.GetByLabel("Learning goals", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.Locator("#product-submit-form button[type=submit]")).ToBeDisabledAsync();
            Assert.False(await db.ProductFormSubmissions.AnyAsync(row => row.FormName == data.Standard.Name));
        }
        finally { await CleanupAsync(data); }
    }

    [Fact]
    public async Task SecurityChallengeStartsOnlyWhenQuestionStepIsShown()
    {
        await using var data = await SignupAsync();
        try
        {
            await OpenAsync(data);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
            await InstallWidgetAsync(waitForReady: false);
            await Expect(Page.Locator("[data-turnstile-ready=true]")).ToHaveCountAsync(0);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Expect(Page.Locator("[data-turnstile-ready=true]")).ToBeVisibleAsync();
            Assert.True(await Page.EvaluateAsync<bool>("() => !!window.testChallenge"));
        }
        finally { await CleanupAsync(data); }
    }

    private static bool IsSignupPost(IResponse response) => response.Request.Method == "POST" && response.Url.Contains("/products/");

    private async Task InstallWidgetAsync(bool blockApi = false, bool waitForReady = true)
    {
        // The local server keeps Turnstile disabled. Exercise its real browser manager with
        // a controlled challenge provider; server-side verification has dedicated HTTP tests.
        await Page.EvaluateAsync("""
            blockApi => {
                const element = document.createElement('div');
                element.setAttribute('data-turnstile-field', '');
                element.dataset.sitekey = 'test-site';
                element.dataset.loadingMessage = 'Completing security check.';
                element.dataset.expiredMessage = 'Security check expired.';
                element.dataset.failedMessage = 'Security check failed.';
                element.dataset.unavailableMessage = 'Security check unavailable.';
                element.innerHTML = '<div data-turnstile-widget></div><input type="hidden" name="cf-turnstile-response" data-turnstile-token><p data-turnstile-status tabindex="-1" hidden></p><button type="button" data-turnstile-retry hidden>Retry security check</button>';
                document.querySelector('[data-signup-step="2"]').append(element);
                window.challengeResets = 0;
                if (!blockApi) window.turnstile = {
                    render: (element, options) => { window.testChallenge = options; return 'widget-1'; },
                    reset: () => { window.challengeResets++; }
                };
            }
            """, blockApi);
        await Page.AddScriptTagAsync(new() { Url = LocalTestApplication.PublicBaseUrl + "/js/turnstile-form.js" });
        if (waitForReady) await Expect(Page.Locator("[data-turnstile-ready=true]")).ToBeVisibleAsync();
    }

    private async Task OpenAsync(CatalogBrowsingFixture data)
    {
        var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{data.Standard.Slug}");
        Assert.Equal(200, response!.Status);
        await Expect(Page.Locator("[data-signup-step='0']")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign up", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-signup-step='0']")).ToBeHiddenAsync();
        await Page.GetByLabel("Full name", new() { Exact = true }).FillAsync("Signup customer");
        await Page.GetByLabel("Email address", new() { Exact = true }).FillAsync(data.User.Email!);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-signup-step='1']")).ToBeHiddenAsync();
        await Page.GetByLabel("Learning goals", new() { Exact = true }).FillAsync("Practise conversation");
    }

    private static async Task AssertSavedOnceAsync(CatalogBrowsingFixture data)
    {
        await using var db = LocalTestApplication.CreateDbContext();
        var saved = await db.ProductFormSubmissions.SingleAsync(row => row.FormName == data.Standard.Name);
        Assert.False(string.IsNullOrWhiteSpace(saved.RequestKey));
        Assert.DoesNotContain("cf-turnstile-response", saved.SubmissionDataJson);
        Assert.Equal(2, await db.CatalogCheckoutMails.CountAsync(row => row.StripeSessionId == "signup:" + saved.RequestKey));
    }

    private static async Task<CatalogBrowsingFixture> SignupAsync(bool administrator = false)
    {
        var data = await CatalogBrowsingFixture.CreateAsync(administrator);
        try
        {
            await using var db = LocalTestApplication.CreateDbContext();
            var product = await db.CatalogProducts.SingleAsync(row => row.Id == data.Standard.Id);
            product.ProductType = CatalogProductType.Signup;
            product.FormFields =
            [
                new() { Key = "name", Label = "Your name", FieldType = CatalogFormFieldType.Text, IsRequired = true },
                new() { Key = "email", Label = "Your email", FieldType = CatalogFormFieldType.Email, IsRequired = true },
                new() { Key = "goals", Label = "Learning goals", FieldType = CatalogFormFieldType.Textarea, IsRequired = true },
                new() { Key = "phone", Label = "Phone number", FieldType = CatalogFormFieldType.Tel, IsRequired = true },
                new() { Key = "address", Label = "Street address", FieldType = CatalogFormFieldType.Text, IsRequired = true }
            ];
            await db.SaveChangesAsync();
            return data;
        }
        catch { await data.DisposeAsync(); throw; }
    }

    private static string FileMailDirectory()
    {
        var directory = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory))
            throw new InvalidOperationException("Set IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY to the local File mail provider's absolute pickup directory.");
        return directory;
    }

    private static async Task AssertQueuedDeliveryAsync(CatalogBrowsingFixture data)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        await using var db = LocalTestApplication.CreateDbContext();
        var key = await db.ProductFormSubmissions.Where(row => row.FormName == data.Standard.Name).Select(row => row.RequestKey).SingleAsync();
        while (DateTime.UtcNow < deadline)
        {
            var sent = await db.CatalogCheckoutMails.AsNoTracking()
                .Where(row => row.StripeSessionId == "signup:" + key && row.SentAtUtc != null).CountAsync();
            if (sent == 2) break;
            await Task.Delay(250);
        }
        var notifications = await db.CatalogCheckoutMails.AsNoTracking().Where(row => row.StripeSessionId == "signup:" + key).ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, notification => Assert.NotNull(notification.SentAtUtc));
        var messages = Directory.EnumerateFiles(FileMailDirectory(), "*.html").Select(File.ReadAllText)
            .Where(body => body.Contains(data.Standard.Name, StringComparison.Ordinal)).ToList();
        Assert.Equal(2, messages.Count);
        foreach (var notification in notifications)
            Assert.Contains(messages, body => body.StartsWith("To: " + notification.Recipient, StringComparison.Ordinal));
        Assert.Contains(messages, body => body.StartsWith("To: " + data.User.Email, StringComparison.Ordinal));
        Assert.All(messages, body =>
        {
            Assert.Contains("Signup customer", body);
            Assert.Contains("mailto:" + data.User.Email, body);
            Assert.Contains("Practise conversation", body);
            Assert.Contains("+31 20 123 4567", System.Net.WebUtility.HtmlDecode(body));
            Assert.Contains("Example Street 10", body);
            Assert.Contains("1234 AB", body);
            Assert.Contains("Amsterdam", body);
            Assert.Contains("Netherlands", body);
        });
    }

    private static async Task CleanupAsync(CatalogBrowsingFixture data)
    {
        await using var db = LocalTestApplication.CreateDbContext();
        var keys = await db.ProductFormSubmissions.Where(row => row.FormName == data.Standard.Name).Select(row => "signup:" + row.RequestKey).ToListAsync();
        await db.ApplicationAuditEvents.Where(row => row.EntityType == "CheckoutSession" && row.EntityId != null && keys.Contains(row.EntityId)).ExecuteDeleteAsync();
        await db.CatalogCheckoutMails.Where(row => keys.Contains(row.StripeSessionId)).ExecuteDeleteAsync();
        await db.ProductFormSubmissions.Where(row => row.FormName == data.Standard.Name).ExecuteDeleteAsync();
        var directory = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY");
        if (directory != null && Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory, "*.html").Where(path => File.ReadAllText(path).Contains(data.Standard.Name, StringComparison.Ordinal)))
                File.Delete(path);
    }
}
