using iD_Develops.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ProductPreviewTests : DiagnosticPageTest
{
    [Theory]
    [InlineData(CatalogProductType.Standard)]
    [InlineData(CatalogProductType.Credit)]
    [InlineData(CatalogProductType.Signup)]
    public async Task PreviewOnlyProductsKeepInformationButRejectStaleForms(CatalogProductType type)
    {
        await using var fixture = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == fixture.Standard.Id);
        product.ProductType = type;
        product.Description = "<p>Product information remains available.</p>";
        await db.SaveChangesAsync();
        var url = $"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}";
        await Page.GotoAsync(url);
        var staleForm = await Page.Locator("input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        Assert.False(string.IsNullOrWhiteSpace(staleForm));
        product.IsPreviewOnly = true;
        await db.SaveChangesAsync();
        await PublicProductSubmissionPacing.ReserveAsync();
        var status = await Page.EvaluateAsync<int>("""
            async token => {
                const body = new URLSearchParams({ __RequestVerificationToken: token });
                return (await fetch(location.href, { method: 'POST', body })).status;
            }
            """, staleForm);
        Assert.Equal(403, status);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-product-preview-only=true]")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = product.Name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Product information remains available.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("#product-submit-form, [data-signup-step], .product-cta")).ToHaveCountAsync(0);
        Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
        product.IsPreviewOnly = false;
        await db.SaveChangesAsync();
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-product-preview-only=true]")).ToHaveCountAsync(0);
        await Expect(Page.Locator(type == CatalogProductType.Signup ? "[data-signup-step='0']" : "#product-submit-form")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AdminCanEnablePreviewOnlyAndTheSettingSurvivesReload()
    {
        await using var fixture = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        var path = $"/products/{fixture.Standard.Slug}/edit";
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await Page.GetByLabel("E-mail").FillAsync(fixture.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(fixture.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
        var preview = Page.GetByRole(AriaRole.Checkbox, new() { Name = "Preview only", Exact = false });
        await Expect(Page.GetByLabel("Update slug when the product name changes")).ToBeEnabledAsync();
        await preview.CheckAsync();
        await Page.RunAndWaitForNavigationAsync(() => Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        await Page.ReloadAsync();
        await Expect(preview).ToBeCheckedAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.True((await db.CatalogProducts.AsNoTracking().SingleAsync(item => item.Id == fixture.Standard.Id)).IsPreviewOnly);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{fixture.Standard.Slug}");
        await Expect(Page.Locator("[data-product-preview-only=true]")).ToBeVisibleAsync();
    }
}
