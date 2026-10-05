namespace iD_Develops.E2ETests;

public sealed class PortalEntitlementSmokeTests : Microsoft.Playwright.Xunit.PageTest
{
    private static string BaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_BASE_URL") ?? "http://localhost:5000").TrimEnd('/');

    [Fact]
    public async Task ProductsPage_RemainsPublic()
    {
        var response = await Page.GotoAsync(
            $"{BaseUrl}/products",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Products returned HTTP {response.Status}.");
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Products & Services" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task PortalCourses_RequiresAuthentication()
    {
        await Page.GotoAsync(
            $"{BaseUrl}/Portal/Courses",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.Contains("/Identity/Account/Login", Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortalCalendar_UsesConsistentRouteAndRequiresAuthentication()
    {
        var response = await Page.GotoAsync(
            $"{BaseUrl}/portal/calendar",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.Contains("/Identity/Account/Login", Page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/portal/calendar", Uri.UnescapeDataString(Page.Url), StringComparison.OrdinalIgnoreCase);
    }
}
