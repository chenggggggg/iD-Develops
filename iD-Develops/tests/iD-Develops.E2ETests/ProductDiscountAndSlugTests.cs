using iD_Develops.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace iD_Develops.E2ETests;

public sealed class ProductDiscountAndSlugTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task AdminCanSaveDiscountsAndAutomaticOrCustomSlugsWithoutCompoundingThePrice()
    {
        await using var fixture = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using (var setup = LocalTestApplication.CreateDbContext())
        {
            var checkoutProduct = await setup.CatalogProducts.SingleAsync(product => product.Id == fixture.Standard.Id);
            checkoutProduct.WorkflowType = CatalogWorkflowType.FormThenStripeCheckout;
            checkoutProduct.Variants.Add(new() { Name = "Checkout", Price = 25, Currency = "EUR", IsDefault = true, IsActive = true });
            await setup.SaveChangesAsync();
        }
        await LoginAsync(fixture);
        var originalSlug = fixture.Standard.Slug;
        var name = $"Updated offer {Guid.NewGuid():N}";
        var slug = name.ToLowerInvariant().Replace(' ', '-');
        await Page.GetByLabel("Update slug when the product name changes").CheckAsync();
        await Page.GetByLabel("Product title", new() { Exact = true }).FillAsync(name);
        await Expect(Page.GetByLabel("Product URL slug", new() { Exact = true })).ToHaveValueAsync(slug);
        await Page.GetByLabel("Product price", new() { Exact = true }).FillAsync("100");
        await Page.GetByLabel("Discount type", new() { Exact = true }).SelectOptionAsync("Percentage");
        await Page.GetByLabel("Discount value", new() { Exact = true }).FillAsync("20");
        await Expect(Page.Locator("[data-discount-preview]")).ToContainTextAsync("EUR 80.00");
        await SaveAsync(slug);
        await Expect(Page.GetByLabel("Product price", new() { Exact = true })).ToHaveValueAsync("100.00");
        await Expect(Page.GetByLabel("Discount value", new() { Exact = true })).ToHaveValueAsync("20.00");
        var customSlug = $"custom-offer-{Guid.NewGuid():N}";
        await Page.GetByLabel("Product URL slug", new() { Exact = true }).FillAsync(customSlug);
        await Page.GetByLabel("Product title", new() { Exact = true }).FillAsync($"Custom title {Guid.NewGuid():N}");
        await Expect(Page.GetByLabel("Update slug when the product name changes")).Not.ToBeCheckedAsync();
        await Expect(Page.GetByLabel("Product URL slug", new() { Exact = true })).ToHaveValueAsync(customSlug);
        await Page.GetByLabel("Discount type", new() { Exact = true }).SelectOptionAsync("FixedAmount");
        await Page.GetByLabel("Discount value", new() { Exact = true }).FillAsync("15");
        await SaveAsync(customSlug);
        await using var db = LocalTestApplication.CreateDbContext();
        var saved = await db.CatalogProducts.SingleAsync(product => product.Id == fixture.Standard.Id);
        Assert.Equal(100m, saved.BasePrice);
        Assert.Equal(CatalogDiscountType.FixedAmount, saved.DiscountType);
        Assert.Equal(15m, saved.DiscountValue);
        Assert.Equal(85m, (await db.CatalogProductVariants.SingleAsync(variant => variant.CatalogProductId == saved.Id)).Price);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{originalSlug}");
        await Expect(Page).ToHaveURLAsync(new Regex($"/products/{customSlug}(?:\\?|$)"));
        await Expect(Page.Locator("#product-unit-price")).ToHaveTextAsync("EUR 85.00");
        await Expect(Page.Locator("#product-original-unit-price")).ToHaveTextAsync("EUR 100.00");
        await Expect(Page.Locator("#product-original-unit-price")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(CatalogDiscountType.Percentage, 20, "80.00", "108.90")]
    [InlineData(CatalogDiscountType.FixedAmount, 15, "85.00", "114.95")]
    public async Task PublicPricesShowDiscountsAndUnchangedFeesAcrossDetailCatalogAndHomepage(CatalogDiscountType type, int value, string unit, string total)
    {
        await using var fixture = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == fixture.Standard.Id);
        product.BasePrice = 100;
        product.DiscountType = type;
        product.DiscountValue = value;
        product.RegistrationFee = 5;
        product.TransactionFee = 3;
        product.ServiceFee = 2;
        product.VatPercentage = 21;
        product.IsFeatured = true;
        product.SortOrder = -1000;
        await db.SaveChangesAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}");
        await Expect(Page.Locator("#product-unit-price")).ToHaveTextAsync($"EUR {unit}");
        await Expect(Page.Locator("#product-total-price")).ToHaveTextAsync($"EUR {total}");
        await Expect(Page.Locator("#product-original-total-price")).ToHaveTextAsync("EUR 133.10");
        await Expect(Page.Locator("#product-original-total-price")).ToBeVisibleAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/pricing");
        var card = Page.GetByRole(AriaRole.Article).Filter(new() { Has = Page.GetByRole(AriaRole.Heading, new() { Name = product.Name, Exact = true }) });
        await Expect(card.Locator("[data-catalog-price] strong")).ToHaveTextAsync($"EUR {total}");
        await Expect(card.GetByLabel("Original price", new() { Exact = true })).ToHaveTextAsync("EUR 133.10");
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/");
        var homepagePrice = Page.Locator("article").Filter(new() { Has = Page.GetByRole(AriaRole.Heading, new() { Name = product.Name, Exact = true }) }).Locator("[data-catalog-price]");
        await Expect(homepagePrice.Locator("strong")).ToHaveTextAsync($"EUR {total}");
        await Expect(homepagePrice.GetByLabel("Original price", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task InvalidDiscountIsRejectedByTheServerWithoutChangingTheProduct()
    {
        await using var fixture = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await LoginAsync(fixture);
        await Page.GetByLabel("Discount type", new() { Exact = true }).SelectOptionAsync("Percentage");
        await Page.GetByLabel("Discount value", new() { Exact = true }).FillAsync("101");
        // Exercise server validation even if a client submits an invalid value directly.
        await Page.EvaluateAsync("() => HTMLFormElement.prototype.submit.call(document.getElementById('product-edit-form'))");
        await Expect(Page.Locator("[data-valmsg-for='EditInput.DiscountValue']")).ToHaveTextAsync("A percentage discount cannot exceed 100%.");
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.Equal(CatalogDiscountType.None, (await db.CatalogProducts.SingleAsync(product => product.Id == fixture.Standard.Id)).DiscountType);
    }

    private async Task LoginAsync(CatalogBrowsingFixture fixture)
    {
        var path = $"/products/{fixture.Standard.Slug}/edit";
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await Page.GetByLabel("E-mail").FillAsync(fixture.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(fixture.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
        await Expect(Page.GetByLabel("Discount value", new() { Exact = true })).ToBeDisabledAsync();
        await Expect(Page.GetByLabel("Update slug when the product name changes")).ToBeEnabledAsync();
    }

    private async Task SaveAsync(string slug)
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(new Regex($"/products/{slug}/edit(?:\\?|$)"));
        await Expect(Page.GetByLabel("Product URL slug", new() { Exact = true })).ToHaveValueAsync(slug);
        await Expect(Page.GetByLabel("Update slug when the product name changes")).ToBeEnabledAsync();
    }
}
