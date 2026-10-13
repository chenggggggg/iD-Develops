namespace iD_Develops.E2ETests;

public sealed class PublicLegalNavigationTests : Microsoft.Playwright.Xunit.PageTest
{
    private static string PublicBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PUBLIC_BASE_URL") ?? "http://id.localhost:5000").TrimEnd('/');

    private static string PortalBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PORTAL_BASE_URL") ?? "http://portal.id.localhost:5000").TrimEnd('/');

    [Theory]
    [InlineData("privacy", "Privacy Policy")]
    [InlineData("cookie-policy", "Cookie Policy")]
    [InlineData("disclaimer", "Disclaimer")]
    [InlineData("terms-conditions", "Terms and Conditions")]
    public async Task EnglishLegalPage_RendersLocalizedContent(string path, string heading)
    {
        var response = await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/{path}",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"{path} returned HTTP {response.Status}.");
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = heading, Exact = true }).First)
            .ToBeVisibleAsync();
        Assert.DoesNotContain("LocalizedHtmlString", await Page.TitleAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("privacy", "Privacybeleid")]
    [InlineData("cookie-policy", "Cookiebeleid")]
    [InlineData("disclaimer", "Disclaimer")]
    [InlineData("terms-conditions", "Algemene voorwaarden")]
    public async Task DutchLegalPage_RendersDutchLocalizedContent(string path, string heading)
    {
        var response = await Page.GotoAsync(
            $"{PublicBaseUrl}/nl-nl/{path}",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"{path} returned HTTP {response.Status}.");
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = heading, Exact = true }).First)
            .ToBeVisibleAsync();
        await Expect(Page.GetByTestId("legal-document"))
            .ToContainTextAsync("Laatst bijgewerkt:");
        await Expect(Page.GetByTestId("legal-document"))
            .Not.ToContainTextAsync("Last updated:");
    }

    [Fact]
    public async Task DutchLegalPage_InternalLinksPreserveDutchCulture()
    {
        await Page.GotoAsync(
            $"{PublicBaseUrl}/nl-nl/cookie-policy",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        await Expect(Page.GetByTestId("legal-document").GetByRole(
                Microsoft.Playwright.AriaRole.Link,
                new() { Name = "Privacybeleid", Exact = true }))
            .ToHaveAttributeAsync("href", "Privacy");
    }

    [Fact]
    public async Task PreferencesLink_HasCookiePolicyFallback()
    {
        await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/privacy",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Preferences", Exact = true }))
            .ToHaveAttributeAsync("href", "/en-us/cookie-policy?preferences=true");
    }

    [Fact]
    public async Task ProductPreview_UsesWorkingPublicLegalNavigation()
    {
        var email = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_EMAIL");
        var password = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await Page.GotoAsync(
            $"{PortalBaseUrl}/login?returnUrl=%2Fproducts",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Page.GetByLabel("E-mail").FillAsync(email);
        await Page.GetByLabel("Password").FillAsync(password);
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{PortalBaseUrl}/products");

        var editLinks = Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Edit", Exact = true });
        if (await editLinks.CountAsync() == 0)
        {
            return;
        }

        var editorHref = await editLinks.First.GetAttributeAsync("href");
        Assert.False(string.IsNullOrWhiteSpace(editorHref));
        var previewUrl = $"{new Uri(new Uri(PortalBaseUrl), editorHref)}?preview=true";
        var previewResponse = await Page.GotoAsync(
            previewUrl,
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(previewResponse);
        Assert.True(previewResponse!.Ok, $"Product preview returned HTTP {previewResponse.Status}.");
        await Expect(Page.Locator(".public-nav-brand img"))
            .ToHaveAttributeAsync("src", "/images/iddevelops-navlogo-dark.png");
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Privacy Policy", Exact = true }))
            .ToHaveAttributeAsync("href", $"{PublicBaseUrl}/en-us/privacy");
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Cookie Policy", Exact = true }))
            .ToHaveAttributeAsync("href", $"{PublicBaseUrl}/en-us/cookie-policy");
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Disclaimer", Exact = true }))
            .ToHaveAttributeAsync("href", $"{PublicBaseUrl}/en-us/disclaimer");
        var preferencesLink = Page.GetByRole(
            Microsoft.Playwright.AriaRole.Link,
            new() { Name = "Preferences", Exact = true });
        await Expect(preferencesLink)
            .ToHaveAttributeAsync("href", $"{PublicBaseUrl}/en-us/cookie-policy?preferences=true");
        await Expect(Page.Locator("#footerLanguageSelector")).ToHaveCountAsync(0);

        await Task.WhenAll(
            Page.WaitForURLAsync($"{PublicBaseUrl}/en-us/cookie-policy?preferences=true"),
            preferencesLink.EvaluateAsync("element => element.click()"));
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Cookie Policy", Exact = true }).First)
            .ToBeVisibleAsync();
    }
}
