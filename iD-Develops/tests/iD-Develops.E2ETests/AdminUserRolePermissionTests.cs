namespace iD_Develops.E2ETests;

public sealed class AdminUserRolePermissionTests : Microsoft.Playwright.Xunit.PageTest
{
    private static string PortalBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PORTAL_BASE_URL") ?? "http://portal.id.localhost:5000").TrimEnd('/');

    [Fact]
    public async Task AdminUsersRequiresAuthentication()
    {
        await Page.GotoAsync(
            $"{PortalBaseUrl}/admin/users",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.StartsWith($"{PortalBaseUrl}/login", Page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/admin/users", Uri.UnescapeDataString(Page.Url), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminCannotEditOwnAdminRole()
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync(administrator: true);

        await Page.GotoAsync(
            $"{PortalBaseUrl}/login?returnUrl=%2Fadmin%2Fusers",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Page.GetByLabel("E-mail").FillAsync(data.User.Email!);
        await Page.GetByLabel("Password").FillAsync(data.Password);
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{PortalBaseUrl}/admin/users");

        var adminRow = Page
            .GetByRole(Microsoft.Playwright.AriaRole.Row)
            .Filter(new Microsoft.Playwright.LocatorFilterOptions { HasText = data.User.Email! });

        await Expect(adminRow).ToHaveCountAsync(1);
        await Expect(adminRow.GetByText("Admin", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(adminRow.GetByRole(Microsoft.Playwright.AriaRole.Button)).ToHaveCountAsync(0);
    }
}
