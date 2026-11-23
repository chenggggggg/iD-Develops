using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PortalLoginRedirectTests : DiagnosticPageTest
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // A stalled consent CDN must not decide whether authentication regressions run.
        // Keep the application, its validation scripts, and all sign-in requests real.
        await Context.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
    }
    [Theory]
    [InlineData("/")]
    [InlineData("/profile?tab=security&source=calendar%2Froster")]
    [InlineData("/calendar?view=week")]
    [InlineData("/account/manage?section=email")]
    public async Task AnonymousNavigationPreservesOriginalLocalDestination(string path)
    {
        var response = await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(200, response!.Status);
        await SiteModeTestSession.AssertLoginRedirectAsync(Page, path);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task StaffLoginReturnsToTheRequestedPageAndQuery(string role)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await SiteModeTestSession.SetRoleAsync(account.User.Id, role);
        const string path = "/profile?tab=security&source=calendar%2Froster";
        await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SiteModeTestSession.AssertLoginRedirectAsync(Page, path);
        await Page.GetByLabel("E-mail").FillAsync(account.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(account.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(LocalTestApplication.PortalBaseUrl + path);
        await Expect(Page.Locator("[data-portal-page-content]")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("xhr")]
    [InlineData("json")]
    public async Task AnonymousProgrammaticRequestsReturn401WithoutLoginHtml(string kind)
    {
        await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + "/login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var result = await Page.EvaluateAsync<string[]>("""
            async kind => {
                const headers = kind === 'xhr' ? {'X-Requested-With':'XMLHttpRequest'} : kind === 'json' ? {'Accept':'application/json'} : {};
                const response = await fetch('/profile?source=api', {headers});
                return [String(response.status), response.url, await response.text()];
            }
            """, kind);
        Assert.Equal("401", result[0]);
        Assert.Equal(LocalTestApplication.PortalBaseUrl + "/profile?source=api", result[1]);
        Assert.DoesNotContain("<form", result[2], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html", result[2], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticatedStaffWithoutPagePermissionGetForbiddenRatherThanLoginLoop()
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password, "/profile", Page.Locator("[data-portal-page-content]"));
        var status = await Page.EvaluateAsync<int>("async () => (await fetch('/admin/audit-log')).status");
        Assert.Equal(403, status);
        await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + "/admin/audit-log", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal("/access-denied", new Uri(Page.Url).AbsolutePath);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Access denied", Exact = true })).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("https://external.example.invalid/path")]
    [InlineData("//external.example.invalid/path")]
    [InlineData("/\\external.example.invalid/path")]
    public async Task LoginRejectsExternalReturnDestinations(string returnUrl)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        var externalRequests = new List<string>();
        await Page.RouteAsync("**://external.example.invalid/**", route =>
        {
            externalRequests.Add(route.Request.Url);
            return route.AbortAsync();
        });
        var response = await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + "/login?returnUrl=" + Uri.EscapeDataString(returnUrl),
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(200, response!.Status);
        await Page.GetByLabel("E-mail").FillAsync(account.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(account.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(LocalTestApplication.PortalBaseUrl + "/");
        await Expect(Page.Locator("[data-portal-page-content]")).ToBeVisibleAsync();
        Assert.Empty(externalRequests);
    }
}