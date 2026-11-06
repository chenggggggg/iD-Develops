namespace iD_Develops.E2ETests;

public sealed class ProductContentLocalizationTests : DiagnosticPageTest
{
    private static string PortalBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PORTAL_BASE_URL") ?? "http://portal.id.localhost:5000").TrimEnd('/');

    [Fact]
    public async Task FreeDownloadEditor_CanSwitchBetweenEnglishAndDutchContent()
    {
        var email = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_EMAIL");
        var password = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        const string editorPath = "/products/free-downloads/edit";
        await BrowserLogin.SignInAsync(Page, email, password, editorPath,
            Page.GetByRole(Microsoft.Playwright.AriaRole.Navigation, new() { Name = "Download content language" }));

        var languageNavigation = Page.GetByRole(
            Microsoft.Playwright.AriaRole.Navigation,
            new() { Name = "Download content language" });
        var englishLink = languageNavigation.GetByRole(
            Microsoft.Playwright.AriaRole.Link,
            new() { Name = "English", Exact = true });
        var dutchLink = languageNavigation.GetByRole(
            Microsoft.Playwright.AriaRole.Link,
            new() { Name = "Dutch", Exact = true });

        await Expect(languageNavigation.GetByText("Content language", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(englishLink).ToHaveAttributeAsync("aria-current", "page");
        await dutchLink.ClickAsync();

        await Expect(Page).ToHaveURLAsync($"{PortalBaseUrl}{editorPath}?contentCulture=nl-NL");
        await Expect(dutchLink).ToHaveAttributeAsync("aria-current", "page");
        await Expect(Page.GetByText("You are editing Dutch download titles and descriptions.", new() { Exact = false }))
            .ToHaveCountAsync(0);
    }

}
