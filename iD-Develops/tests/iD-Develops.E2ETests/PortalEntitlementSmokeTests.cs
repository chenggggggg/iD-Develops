namespace iD_Develops.E2ETests;

public sealed class PortalEntitlementSmokeTests : Microsoft.Playwright.Xunit.PageTest
{
    private static string PublicBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PUBLIC_BASE_URL") ?? "http://id.localhost:5000").TrimEnd('/');

    private static string PortalBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PORTAL_BASE_URL") ?? "http://portal.id.localhost:5000").TrimEnd('/');

    [Fact]
    public async Task ProductsPage_RemainsPublic()
    {
        var response = await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/products",
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
            $"{PortalBaseUrl}/courses",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.StartsWith($"{PortalBaseUrl}/login", Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortalCalendar_UsesConsistentRouteAndRequiresAuthentication()
    {
        var response = await Page.GotoAsync(
            $"{PortalBaseUrl}/calendar",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.StartsWith($"{PortalBaseUrl}/login", Page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/calendar", Uri.UnescapeDataString(Page.Url), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MainDomain_DoesNotExposePortalRoutes()
    {
        using var response = await SendWithoutRedirectAsync($"{PublicBaseUrl}/en-us/courses");

        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("/en-us/error?code=not-found", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortalDomain_DoesNotExposePublicRoutes()
    {
        using var response = await SendWithoutRedirectAsync($"{PortalBaseUrl}/en-us/products");

        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("/error?code=not-found", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductEditor_IsProtectedByPortalLogin()
    {
        await Page.GotoAsync(
            $"{PortalBaseUrl}/products/example/edit",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.StartsWith($"{PortalBaseUrl}/login", Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortalHome_UsesPortalModuleAndRequiresAuthentication()
    {
        await Page.GotoAsync(
            $"{PortalBaseUrl}/",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.StartsWith($"{PortalBaseUrl}/login", Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortalProductManagement_UsesPortalModuleAndRequiresAuthentication()
    {
        await Page.GotoAsync(
            $"{PortalBaseUrl}/products",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.StartsWith($"{PortalBaseUrl}/login", Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicErrorPage_RendersWithoutEndpointAmbiguity()
    {
        var response = await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/error?code=not-found&statusCode=404",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.Equal(404, response!.Status);
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Page not found" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task PublicNavigation_GeneratesAndFollowsLinksToOtherPages()
    {
        var response = await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Home returned HTTP {response.Status}.");

        var enterpriseLink = Page.GetByRole(
            Microsoft.Playwright.AriaRole.Link,
            new() { Name = "For organizations", Exact = true }).First;
        await Expect(enterpriseLink).ToHaveAttributeAsync("href", "/en-us/enterprise");

        await enterpriseLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{PublicBaseUrl}/en-us/enterprise");
    }

    [Fact]
    public async Task PublicNavigation_PreservesDutchCultureAcrossPages()
    {
        var response = await Page.GotoAsync(
            $"{PublicBaseUrl}/nl-nl/",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Dutch home returned HTTP {response.Status}.");

        var enterpriseLink = Page.GetByRole(
            Microsoft.Playwright.AriaRole.Link,
            new() { Name = "Voor organisaties", Exact = true }).First;
        await Expect(enterpriseLink).ToHaveAttributeAsync("href", "/nl-nl/enterprise");

        await enterpriseLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{PublicBaseUrl}/nl-nl/enterprise");
    }

    [Fact]
    public async Task PublicNavigation_OffersPortalLoginWhenNoSessionIsKnown()
    {
        var response = await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Home returned HTTP {response.Status}.");
        var loginLink = Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Log In" }).First;
        await Expect(loginLink).ToBeVisibleAsync();
        await Expect(loginLink).ToHaveAttributeAsync(
            "href",
            new System.Text.RegularExpressions.Regex(
                $"^{System.Text.RegularExpressions.Regex.Escape(PortalBaseUrl)}/login\\?returnUrl="));
    }

    [Fact]
    public async Task PublicNavigation_ShowsPortalAfterSuccessfulLogin()
    {
        var email = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_EMAIL");
        var password = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await Page.GotoAsync(
            $"{PortalBaseUrl}/login?returnUrl=%2F",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Page.GetByLabel("E-mail").FillAsync(email);
        await Page.GetByLabel("Password").FillAsync(password);
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(url =>
            url.StartsWith(PortalBaseUrl, StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/login", StringComparison.OrdinalIgnoreCase));

        var response = await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Home returned HTTP {response.Status} after login.");
        var portalLink = Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Portal", Exact = true }).First;
        await Expect(portalLink).ToBeVisibleAsync();
        await Expect(portalLink).ToHaveAttributeAsync("href", $"{PortalBaseUrl}/");
    }

    [Fact]
    public async Task LocalBrowser_AcceptsSharedCookieFromPortalResponse()
    {
        const string localPublicUrl = "http://id.localhost:5000";
        const string localPortalUrl = "http://portal.id.localhost:5000";
        const string probeCookieName = "iddevelops-session-indicator-probe";
        var probeUrl = $"{localPortalUrl}/session-cookie-probe";
        await Page.RouteAsync(probeUrl, async route =>
        {
            await route.FulfillAsync(new Microsoft.Playwright.RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/html",
                Body = "<!doctype html><title>Cookie probe</title>",
                Headers = new Dictionary<string, string>
                {
                    ["Set-Cookie"] = $"{probeCookieName}=active; Domain=.id.localhost; Path=/; HttpOnly; SameSite=Lax"
                }
            });
        });

        var response = await Page.GotoAsync(probeUrl);
        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Cookie probe returned HTTP {response.Status}.");

        var publicCookies = await Context.CookiesAsync([localPublicUrl]);
        var portalCookies = await Context.CookiesAsync([localPortalUrl]);

        Assert.Contains(publicCookies, cookie => cookie.Name == probeCookieName);
        Assert.Contains(portalCookies, cookie => cookie.Name == probeCookieName);
    }

    [Fact]
    public async Task PortalErrorPage_RendersWithoutEndpointAmbiguity()
    {
        var response = await Page.GotoAsync(
            $"{PortalBaseUrl}/error?code=not-found&statusCode=404",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.Equal(404, response!.Status);
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Page not found" }))
            .ToBeVisibleAsync();
    }

    private static async Task<HttpResponseMessage> SendWithoutRedirectAsync(string url)
    {
        var target = new Uri(url);
        var connectTo = target;
        if (target.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(target) { Host = "127.0.0.1" };
            connectTo = builder.Uri;
        }

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, connectTo);
        request.Headers.Host = target.Authority;
        return await client.SendAsync(request);
    }
}
