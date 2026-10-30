using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace iD_Develops.E2ETests;

public sealed class ProductCheckoutProtectionTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task ConcurrentCheckoutPostsCreateOnlyOneCompletedOrder()
    {
        await using var fixture = await PaidProductAsync();
        try
        {
            await OpenAsync(fixture);
            await Page.GetByLabel("Your email", new() { Exact = true }).FillAsync(fixture.User.Email!);
            await PublicProductSubmissionPacing.ReserveAsync(2);
            var statuses = await Page.EvaluateAsync<int[]>("""
                async () => {
                    const body = new URLSearchParams(new FormData(document.getElementById('product-submit-form')));
                    const responses = await Promise.all([0, 1].map(() => fetch(location.href, { method: 'POST', body })));
                    return responses.map(response => response.status);
                }
                """);
            Assert.All(statuses, status => Assert.Equal(200, status));
            await using var db = LocalTestApplication.CreateDbContext();
            var attempt = await db.CatalogCheckoutAttempts.SingleAsync(row => row.CatalogProductId == fixture.Standard.Id);
            Assert.NotNull(attempt.CompletedAtUtc);
            Assert.Equal(1, await db.ProductFormSubmissions.CountAsync(row => row.FormName == fixture.Standard.Name));
            Assert.Equal(2, await db.CatalogCheckoutMails.CountAsync(row => row.StripeSessionId == attempt.StripeSessionId));
        }
        finally { await CleanupAsync(fixture); }
    }
    [Fact]
    public async Task PaidProductDraftSurvivesNavigationAndReloadWithoutTurnstile()
    {
        await using var fixture = await PaidProductAsync();
        await Page.RouteAsync("https://challenges.cloudflare.com/**", route => route.AbortAsync());
        await OpenAsync(fixture);
        await Expect(Page.Locator(".cf-turnstile, script[src*='challenges.cloudflare.com/turnstile']")).ToHaveCountAsync(0);
        await Page.GetByLabel("Your name", new() { Exact = true }).FillAsync("Returning customer");
        await Page.GetByLabel("Your email", new() { Exact = true }).FillAsync(fixture.User.Email!);
        await Page.GetByLabel("Your message", new() { Exact = true }).FillAsync("Keep my details\nwhile I decide.");
        await Page.GetByLabel("Preferred format", new() { Exact = true }).SelectOptionAsync("In person");
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Conversation", Exact = true }).CheckAsync();
        var requestId = await Page.Locator("[name=CheckoutRequestId]").InputValueAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/pricing");
        await OpenAsync(fixture);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-draft-ready=true]")).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Your name", new() { Exact = true })).ToHaveValueAsync("Returning customer");
        await Expect(Page.GetByLabel("Your email", new() { Exact = true })).ToHaveValueAsync(fixture.User.Email!);
        await Expect(Page.GetByLabel("Your message", new() { Exact = true })).ToHaveValueAsync("Keep my details\nwhile I decide.");
        await Expect(Page.GetByLabel("Preferred format", new() { Exact = true })).ToHaveValueAsync("In person");
        await Expect(Page.GetByRole(AriaRole.Checkbox, new() { Name = "Conversation", Exact = true })).ToBeCheckedAsync();
        await Expect(Page.Locator("[name=CheckoutRequestId]")).ToHaveValueAsync(requestId);
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(row => row.CatalogProductId == fixture.Standard.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalCheckoutCompletesWithCloudflareBlockedAndOptionalDraftStorage(bool blockStorage)
    {
        await using var fixture = await PaidProductAsync();
        await Page.RouteAsync("https://challenges.cloudflare.com/**", route => route.AbortAsync());
        if (blockStorage)
            await Context.AddInitScriptAsync("Object.defineProperty(window, 'sessionStorage', { get() { throw new Error('Storage unavailable'); } });");
        try
        {
            await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{fixture.Standard.Slug}");
            if (!blockStorage) await Expect(Page.Locator("[data-draft-ready=true]")).ToBeVisibleAsync();
            await Page.GetByLabel("Your name", new() { Exact = true }).FillAsync("Checkout customer");
            await Page.GetByLabel("Your email", new() { Exact = true }).FillAsync(fixture.User.Email!);
            await Page.GetByLabel("Your message", new() { Exact = true }).FillAsync(new string('x', 1500));
            await PublicProductSubmissionPacing.ReserveAsync();
            await Page.Locator("#product-submit-form button[type=submit]").ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex("/payment-result\\?.*local=true"));
            await Expect(Page.Locator($"[data-completed-checkout-product-id='{fixture.Standard.Id}']")).ToBeVisibleAsync();
            if (!blockStorage)
                await Page.WaitForFunctionAsync("key => sessionStorage.getItem(key) === null", $"catalog-checkout-draft:{fixture.Standard.Id}");
            await using var db = LocalTestApplication.CreateDbContext();
            var attempt = await db.CatalogCheckoutAttempts.SingleAsync(row => row.CatalogProductId == fixture.Standard.Id);
            Assert.NotNull(attempt.CompletedAtUtc);
            Assert.Equal(1, await db.ProductFormSubmissions.CountAsync(row => row.FormName == fixture.Standard.Name));
            Assert.True(await db.CatalogCheckoutReceipts.AnyAsync(row => row.StripeSessionId == attempt.StripeSessionId));
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    private static async Task CleanupAsync(CatalogBrowsingFixture fixture)
    {
        await using var cleanup = LocalTestApplication.CreateDbContext();
        var sessions = await cleanup.CatalogCheckoutAttempts.Where(row => row.CatalogProductId == fixture.Standard.Id)
            .Select(row => row.StripeSessionId).ToListAsync();
        await cleanup.CatalogCheckoutMails.Where(row => sessions.Contains(row.StripeSessionId)).ExecuteDeleteAsync();
        await cleanup.CatalogCheckoutReceipts.Where(row => sessions.Contains(row.StripeSessionId)).ExecuteDeleteAsync();
        await cleanup.ProductFormSubmissions.Where(row => row.FormName == fixture.Standard.Name).ExecuteDeleteAsync();
    }

    private async Task OpenAsync(CatalogBrowsingFixture fixture)
    {
        var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{fixture.Standard.Slug}");
        Assert.Equal(200, response!.Status);
        await Expect(Page.Locator("[data-draft-ready=true]")).ToBeVisibleAsync();
    }

    private static async Task<CatalogBrowsingFixture> PaidProductAsync()
    {
        var fixture = await CatalogBrowsingFixture.CreateAsync();
        try
        {
            await using var setup = LocalTestApplication.CreateDbContext();
            var product = await setup.CatalogProducts.SingleAsync(row => row.Id == fixture.Standard.Id);
            product.WorkflowType = CatalogWorkflowType.FormThenStripeCheckout;
            product.Variants.Add(new() { Name = "Standard", Price = 25, Currency = "EUR", IsDefault = true, IsActive = true });
            product.FormFields =
            [
                new() { Key = "name", Label = "Your name", FieldType = CatalogFormFieldType.Text },
                new() { Key = "email", Label = "Your email", FieldType = CatalogFormFieldType.Email, IsRequired = true },
                new() { Key = "message", Label = "Your message", FieldType = CatalogFormFieldType.Textarea, CharacterLimit = 2000 },
                new() { Key = "format", Label = "Preferred format", FieldType = CatalogFormFieldType.Select, OptionsText = "Online\nIn person" },
                new() { Key = "topics", Label = "Topics", FieldType = CatalogFormFieldType.Checkbox, AllowMultipleOptions = true, OptionsText = "plain\tConversation" }
            ];
            await setup.SaveChangesAsync();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
}
