using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ProductBrowsingTests : Microsoft.Playwright.Xunit.PageTest
{
    [Theory]
    [InlineData("en-us")]
    [InlineData("nl-nl")]
    public async Task PublicCatalog_FiltersCreditsAndOpensProductForm(string culture)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        var listingUrl = $"{CatalogBrowsingFixture.PublicBaseUrl}/{culture}/products";
        var response = await Page.GotoAsync(listingUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Credit.Name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Standard.Name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Draft.Name, Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Hidden.Name, Exact = true })).ToHaveCountAsync(0);

        response = await Page.GotoAsync($"{listingUrl}?type=credit", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Standard.Name, Exact = true })).ToHaveCountAsync(0);
        var card = Page.GetByRole(AriaRole.Article).Filter(new()
        {
            Has = Page.GetByRole(AriaRole.Heading, new() { Name = data.Credit.Name, Exact = true })
        });
        await card.GetByRole(AriaRole.Link).ClickAsync();

        await Expect(Page).ToHaveURLAsync($"{CatalogBrowsingFixture.PublicBaseUrl}/{culture}/products/{data.Credit.Slug}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Credit.Name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Your name", new() { Exact = true })).ToHaveValueAsync(string.Empty);
        await Expect(Page.GetByLabel("Your message", new() { Exact = true })).ToHaveValueAsync(string.Empty);
        await Expect(Page.GetByLabel("Preferred format", new() { Exact = true })).ToHaveValueAsync(string.Empty);
        await Expect(Page.GetByRole(AriaRole.Checkbox, new() { Name = "Conversation", Exact = true })).Not.ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Morning", Exact = true })).Not.ToBeCheckedAsync();
    }

    [Theory]
    [InlineData("en-us")]
    [InlineData("nl-nl")]
    public async Task ProductForm_ValidationFailure_PreservesSubmittedValues(string culture)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        var productUrl = $"{CatalogBrowsingFixture.PublicBaseUrl}/{culture}/products/{data.Credit.Slug}";
        await Page.GotoAsync(productUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.GetByLabel("Your name", new() { Exact = true }).FillAsync("Client <&> Name");
        await Page.GetByLabel("Telephone", new() { Exact = true }).FillAsync("0123456789");
        await Page.GetByLabel("Your message", new() { Exact = true }).FillAsync("First line\nSecond line <&>");
        await Page.GetByLabel("Preferred format", new() { Exact = true }).SelectOptionAsync("In person");
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Other topic", Exact = true }).CheckAsync();
        await Page.Locator("[name='topics_1_text']").FillAsync("Business Dutch");
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Evening", Exact = true }).CheckAsync();
        await Page.Locator("[name='items_0']").FillAsync("Speak confidently");

        // Leave the required email empty so validation returns the form without sending email or starting checkout.
        await PublicProductSubmissionPacing.ReserveAsync();
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.Locator("#product-submit-form").GetByRole(AriaRole.Button).ClickAsync(),
            response => response.Request.Method == "POST" && response.Url == productUrl);

        Assert.Equal(200, response.Status);
        var validationMessage = culture == "nl-nl" ? "Your email is verplicht." : "Your email is required.";
        await Expect(Page.Locator("#product-submit-form").GetByText(validationMessage, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Your name", new() { Exact = true })).ToHaveValueAsync("Client <&> Name");
        await Expect(Page.GetByLabel("Telephone", new() { Exact = true })).ToHaveValueAsync("0123456789");
        await Expect(Page.GetByLabel("Your message", new() { Exact = true })).ToHaveValueAsync("First line\nSecond line <&>");
        await Expect(Page.GetByLabel("Preferred format", new() { Exact = true })).ToHaveValueAsync("In person");
        await Expect(Page.GetByRole(AriaRole.Checkbox, new() { Name = "Other topic", Exact = true })).ToBeCheckedAsync();
        await Expect(Page.Locator("[name='topics_1_text']")).ToHaveValueAsync("Business Dutch");
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Evening", Exact = true })).ToBeCheckedAsync();
        await Expect(Page.Locator("[name='items_0']")).ToHaveValueAsync("Speak confidently");
    }

    [Fact]
    public async Task Profile_AddCredits_OpensPublicCreditCatalog()
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        await Page.GotoAsync($"{CatalogBrowsingFixture.PortalBaseUrl}/login?returnUrl=%2Fprofile",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.GetByLabel("E-mail").FillAsync(data.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(data.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{CatalogBrowsingFixture.PortalBaseUrl}/profile");
        var addCredits = Page.GetByRole(AriaRole.Link, new() { Name = "Add credits", Exact = true });
        var expectedUrl = $"{CatalogBrowsingFixture.PublicBaseUrl}/en-us/products?type=credit";

        await Expect(addCredits).ToHaveAttributeAsync("href", expectedUrl);
        await addCredits.ClickAsync();

        await Expect(Page).ToHaveURLAsync(expectedUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Credit.Name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Standard.Name, Exact = true })).ToHaveCountAsync(0);
    }
}
