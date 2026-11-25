using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PublicProductPreviewContactTests : DiagnosticPageTest
{
    [Theory]
    [InlineData(CatalogProductType.Standard, "en-us", false)]
    [InlineData(CatalogProductType.Standard, "nl-nl", true)]
    [InlineData(CatalogProductType.Signup, "en-us", false)]
    [InlineData(CatalogProductType.Signup, "nl-nl", true)]
    [InlineData(CatalogProductType.Booking, "en-us", false)]
    [InlineData(CatalogProductType.Booking, "nl-nl", true)]
    public async Task PreviewProductOffersLocalizedContactInsteadOfTransaction(CatalogProductType type, string culture, bool mobile)
    {
        await using var fixture = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == fixture.Standard.Id);
        product.ProductType = type;
        product.IsPreviewOnly = true;
        product.IsSalesActive = false;
        product.WorkflowType = CatalogWorkflowType.FormThenExternalBooking;
        product.ExternalBookingUrl = "https://koalendar.com/e/preview-must-not-book";
        product.FormFields = [new CatalogProductFormField { Key = "goals", Label = "Goals", FieldType = CatalogFormFieldType.Text }];
        await db.SaveChangesAsync();
        if (mobile) await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/products/{product.Slug}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = product.Name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("#product-submit-form, [data-signup-step]")).ToHaveCountAsync(0);
        var contact = Page.Locator("[data-product-contact-action]");
        await Expect(contact).ToHaveTextAsync(culture == "nl-nl" ? "Neem contact op →" : "Contact us →");
        await Expect(contact).ToHaveAttributeAsync("href", $"/{culture}/contact");
        await Expect(contact).ToBeVisibleAsync();
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await contact.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
        await Expect(Page.Locator("#DisplayName")).ToBeVisibleAsync();
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
        Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
    }

    [Fact]
    public async Task StaffPreviewContactLeavesThePortalForThePublicSite()
    {
        const string culture = "en-us"; // The staff portal deliberately uses English.
        await using var fixture = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == fixture.Standard.Id);
        product.IsPreviewOnly = true;
        await db.SaveChangesAsync();
        var path = $"/products/{product.Slug}/edit?preview=true";
        await BrowserLogin.SignInAsync(Page, fixture.User.Email!, fixture.Password, path, Page.Locator("[data-product-contact-action]"));
        var contact = Page.Locator("[data-product-contact-action]");
        await Expect(contact).ToHaveAttributeAsync("href", $"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
        await contact.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
    }

    [Theory]
    [InlineData("en-us", true)]
    [InlineData("nl-nl", true)]
    [InlineData("en-us", false)]
    [InlineData("nl-nl", false)]
    public async Task PreviewDownloadCardOffersContactEvenWithoutAFile(string culture, bool hasFile)
    {
        await using var fixture = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == fixture.Standard.Id);
        product.ProductType = CatalogProductType.FreeDownload;
        product.IsPreviewOnly = true;
        product.IncludedBookingBenefitUrl = hasFile ? "https://downloads.example.invalid/preview.pdf" : null;
        await db.SaveChangesAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/free-downloads");
        var card = Page.GetByRole(AriaRole.Article).Filter(new()
        {
            Has = Page.GetByRole(AriaRole.Heading, new() { Name = product.Name, Exact = true })
        });
        var contact = card.GetByRole(AriaRole.Link);
        await Expect(contact).ToHaveCountAsync(1);
        await Expect(contact).ToHaveAttributeAsync("href", $"/{culture}/contact");
        var handlerUrl = $"{LocalTestApplication.PublicBaseUrl}/{culture}/free-downloads?handler=Download&id={product.Id}";
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.EvaluateAsync("async url => { await fetch(url, {redirect:'manual'}); }", handlerUrl),
            response => response.Url == handlerUrl);
        Assert.Equal(404, response.Status);
        await contact.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
    }
}
