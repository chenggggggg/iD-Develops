using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PublicLanguageSwitchNoScriptTests : DiagnosticPageTest
{
    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions();
        options.JavaScriptEnabled = false;
        return options;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Test native navigation using first-party styling. External font/consent delivery
        // is unrelated to the fallback link, and delayed stylesheets can block no-script rendering.
        await Context.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        foreach (var stylesheet in new[] { "https://fonts.googleapis.com/**", "https://cdnjs.cloudflare.com/**" })
            await Context.RouteAsync(stylesheet, route => route.FulfillAsync(new() { ContentType = "text/css", Body = string.Empty }));
        await Context.RouteAsync("https://fonts.gstatic.com/**", route => route.AbortAsync());
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task LanguageSwitchWorksAsANativeLinkWithoutJavaScript(int width)
    {
        await Page.SetViewportSizeAsync(width, 900);
        const string suffix = "/about?ref=no-script";
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us{suffix}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var toggle = Page.Locator("[data-public-language-switch]:visible");
        await Expect(toggle).ToHaveCountAsync(1);
        await Expect(toggle).ToBeVisibleAsync();
        await Expect(toggle).ToHaveAccessibleNameAsync("Switch to Dutch");
        await Expect(toggle).ToHaveAttributeAsync("href", $"/nl-nl{suffix}");
        await toggle.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/nl-nl{suffix}");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "nl-NL");
        await Expect(toggle).ToHaveAccessibleNameAsync("Schakel over naar Engels");
        await Expect(toggle).ToHaveAttributeAsync("href", $"/en-us{suffix}");
        await toggle.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/en-us{suffix}");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "en-US");
    }
}
