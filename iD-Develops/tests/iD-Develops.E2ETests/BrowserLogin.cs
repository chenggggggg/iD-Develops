using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

internal static class BrowserLogin
{
    public static async Task SignInAsync(IPage page, string email, string password, string returnPath, ILocator ready, Func<string, bool>? expectedUrl = null)
    {
        var destination = $"{LocalTestApplication.PortalBaseUrl}{returnPath}";
        var response = await page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(returnPath)}",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Login page returned HTTP {response.Status}.");
        await page.GetByLabel("E-mail").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        var matchesDestination = expectedUrl ?? (url => url == destination);
        var destinationResponse = page.WaitForResponseAsync(result =>
            result.Request.IsNavigationRequest && result.Request.Method == "GET" &&
            result.Status is not (>= 300 and < 400) && matchesDestination(result.Url));
        await Task.WhenAll(
            page.WaitForURLAsync(matchesDestination, new() { WaitUntil = WaitUntilState.DOMContentLoaded }),
            page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync(new() { NoWaitAfter = true }));
        var loaded = await destinationResponse;
        Assert.True(loaded.Ok, $"Login destination returned HTTP {loaded.Status}; request ID: {loaded.Headers.GetValueOrDefault("x-request-id", "unavailable")}.");
        await Assertions.Expect(ready).ToBeVisibleAsync();
        await Assertions.Expect(ready).ToBeEnabledAsync();
    }
}
