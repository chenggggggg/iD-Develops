using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class SignupTurnstileTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task SignupReturnsSuccessOnFirstClickAndQueuesBothNotifications()
    {
        await using var data = await SignupAsync();
        try
        {
            await OpenAsync(data);
            await PublicProductSubmissionPacing.ReserveAsync();
            await Page.RunAndWaitForResponseAsync(() => Page.Locator("#product-submit-form button[type=submit]").ClickAsync(), IsSignupPost);
            await Expect(Page.GetByText("Your submission has been received.", new() { Exact = true })).ToBeVisibleAsync();
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
            await Expect(Page.GetByText("Your submission has been received.", new() { Exact = true })).ToBeVisibleAsync();
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
            await Expect(Page.GetByText("Your submission has been received.", new() { Exact = true })).ToBeVisibleAsync();
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

    private static bool IsSignupPost(IResponse response) => response.Request.Method == "POST" && response.Url.Contains("/products/");

    private async Task InstallWidgetAsync(bool blockApi = false)
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
                document.getElementById('product-submit-form').append(element);
                window.challengeResets = 0;
                if (!blockApi) window.turnstile = {
                    render: (element, options) => { window.testChallenge = options; return 'widget-1'; },
                    reset: () => { window.challengeResets++; }
                };
            }
            """, blockApi);
        await Page.AddScriptTagAsync(new() { Url = LocalTestApplication.PublicBaseUrl + "/js/turnstile-form.js" });
        await Expect(Page.Locator("[data-turnstile-ready=true]")).ToBeVisibleAsync();
    }

    private async Task OpenAsync(CatalogBrowsingFixture data)
    {
        var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{data.Standard.Slug}");
        Assert.Equal(200, response!.Status);
        await Page.GetByLabel("Your name", new() { Exact = true }).FillAsync("Signup customer");
        await Page.GetByLabel("Your email", new() { Exact = true }).FillAsync(data.User.Email!);
    }

    private static async Task AssertSavedOnceAsync(CatalogBrowsingFixture data)
    {
        await using var db = LocalTestApplication.CreateDbContext();
        var saved = await db.ProductFormSubmissions.SingleAsync(row => row.FormName == data.Standard.Name);
        Assert.False(string.IsNullOrWhiteSpace(saved.RequestKey));
        Assert.DoesNotContain("cf-turnstile-response", saved.SubmissionDataJson);
        Assert.Equal(2, await db.CatalogCheckoutMails.CountAsync(row => row.StripeSessionId == "signup:" + saved.RequestKey));
    }

    private static async Task<CatalogBrowsingFixture> SignupAsync()
    {
        var data = await CatalogBrowsingFixture.CreateAsync();
        try
        {
            await using var db = LocalTestApplication.CreateDbContext();
            var product = await db.CatalogProducts.SingleAsync(row => row.Id == data.Standard.Id);
            product.ProductType = CatalogProductType.Signup;
            product.FormFields =
            [
                new() { Key = "name", Label = "Your name", FieldType = CatalogFormFieldType.Text, IsRequired = true },
                new() { Key = "email", Label = "Your email", FieldType = CatalogFormFieldType.Email, IsRequired = true }
            ];
            await db.SaveChangesAsync();
            return data;
        }
        catch { await data.DisposeAsync(); throw; }
    }

    private static async Task CleanupAsync(CatalogBrowsingFixture data)
    {
        await using var db = LocalTestApplication.CreateDbContext();
        var keys = await db.ProductFormSubmissions.Where(row => row.FormName == data.Standard.Name).Select(row => "signup:" + row.RequestKey).ToListAsync();
        await db.CatalogCheckoutMails.Where(row => keys.Contains(row.StripeSessionId)).ExecuteDeleteAsync();
        await db.ProductFormSubmissions.Where(row => row.FormName == data.Standard.Name).ExecuteDeleteAsync();
    }
}
