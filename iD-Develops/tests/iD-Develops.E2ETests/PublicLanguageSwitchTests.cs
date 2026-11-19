using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PublicLanguageSwitchTests : DiagnosticPageTest
{
    [Theory]
    [InlineData(320)]
    [InlineData(768)]
    [InlineData(1024)]
    [InlineData(1440)]
    public async Task NavbarSwitchesBothWaysWithoutDropdownAndPreservesLocation(int width)
    {
        await Page.SetViewportSizeAsync(width, 900);
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        const string suffix = "/about?ref=language%2Btest&value=two#language-check";
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us{suffix}");
        var toggle = Page.Locator("[data-public-language-switch]:visible");
        await Expect(toggle).ToHaveCountAsync(1);
        await Expect(toggle).ToHaveAccessibleNameAsync("Switch to Dutch");
        await Expect(toggle).ToHaveAttributeAsync("href", "/nl-nl/about?ref=language%2Btest&value=two");
        await Expect(toggle).ToContainTextAsync("EN");
        var bounds = await toggle.BoundingBoxAsync();
        Assert.True(bounds!.Width >= 44 && bounds.Height >= 44);
        Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await toggle.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/nl-nl{suffix}");
        await Expect(toggle).ToHaveAccessibleNameAsync("Schakel over naar Engels");
        await Expect(toggle).ToContainTextAsync("NL");
        Assert.Contains(await Context.CookiesAsync(), cookie => cookie.Name == "ASPNET_LANG" && cookie.Value == "nl-NL");
        await toggle.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/en-us{suffix}");
        await Expect(toggle).ToHaveAccessibleNameAsync("Switch to Dutch");
    }
}
