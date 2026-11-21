using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PublicMobileNavigationTests : DiagnosticPageTest
{
    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions();
        options.HasTouch = true;
        return options;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Navigation must work without consent or third-party font delivery.
        await Context.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        foreach (var stylesheet in new[] { "https://fonts.googleapis.com/**", "https://cdnjs.cloudflare.com/**" })
            await Context.RouteAsync(stylesheet, route => route.FulfillAsync(new() { ContentType = "text/css", Body = string.Empty }));
        await Context.RouteAsync("https://fonts.gstatic.com/**", route => route.AbortAsync());
    }

    [Theory]
    [InlineData("en-us", "/", 320, 740)]
    [InlineData("nl-nl", "/", 390, 844)]
    [InlineData("en-us", "/", 768, 1024)]
    [InlineData("nl-nl", "/", 844, 390)]
    [InlineData("nl-nl", "/about", 320, 740)]
    [InlineData("en-us", "/about", 390, 844)]
    [InlineData("nl-nl", "/about", 768, 1024)]
    [InlineData("en-us", "/about", 844, 390)]
    public async Task DrawerFillsTheViewportAndItsNavigationRemainsReachable(string culture, string path, int width, int height)
    {
        await OpenPageAsync(culture, path, width, height);
        var toggle = Page.Locator("[data-public-menu-toggle]");
        await toggle.TapAsync();

        var menu = Page.GetByRole(AriaRole.Dialog, new() { Name = "Menu", Exact = true });
        await Expect(menu).ToBeVisibleAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        foreach (var destination in new[] { "nt2-integration", "personal-development", "language-and-communication", "enterprise", "pricing", "contact" })
            await Expect(menu.Locator($"a[href='/{culture}/{destination}']")).ToBeVisibleAsync();
        var bounds = await menu.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.InRange(bounds.Y, -1, 1);
        Assert.InRange(bounds.Height, height - 1, height + 1);
        Assert.True(bounds.X >= 0 && bounds.X + bounds.Width <= width + 1,
            "The navigation drawer must fit horizontally inside the viewport.");

        var contact = menu.Locator($"a[href='/{culture}/contact']");
        await contact.ScrollIntoViewIfNeededAsync();
        await Expect(contact).ToBeInViewportAsync(new() { Ratio = 1 });
        Assert.True(await menu.GetByRole(AriaRole.Link).EvaluateAllAsync<bool>(
            "links => links.every(link => link.getBoundingClientRect().height >= 44)"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }

    [Fact]
    public async Task ContactAndAccountActionsStaySeparateAndMarketingHidesLogin()
    {
        var version = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/health/version");
        Assert.Equal(200, version!.Status);
        var mode = (await version.JsonAsync())!.Value.GetProperty("siteMode").GetString();
        await OpenPageAsync("en-us", "/about", 844, 390);
        var toggle = Page.Locator("[data-public-menu-toggle]");
        await toggle.TapAsync();
        var menu = Page.Locator("[data-public-menu]");
        var navigation = menu.GetByRole(AriaRole.Navigation, new() { Name = "Menu", Exact = true });
        var actions = menu.Locator("[data-public-mobile-actions]");
        var contact = actions.GetByRole(AriaRole.Link, new() { Name = "Contact", Exact = true });
        await Expect(contact).ToBeInViewportAsync(new() { Ratio = 1 });
        await Expect(navigation.GetByRole(AriaRole.Link)).ToHaveCountAsync(5);
        await navigation.Locator("a[href='/en-us/pricing']").ScrollIntoViewIfNeededAsync();
        await Expect(navigation.Locator("a[href='/en-us/pricing']")).ToBeInViewportAsync(new() { Ratio = 1 });
        await Expect(contact).ToBeInViewportAsync(new() { Ratio = 1 });

        var navigationBounds = await navigation.BoundingBoxAsync();
        var actionBounds = await actions.BoundingBoxAsync();
        Assert.True(navigationBounds!.Y + navigationBounds.Height <= actionBounds!.Y,
            "Contact and account actions must have their own area below the scrolling navigation.");

        var login = actions.GetByRole(AriaRole.Link, new() { Name = "Log In", Exact = true });
        await Expect(login).ToHaveCountAsync(mode == "Marketing" ? 0 : 1);
        if (mode != "Marketing") await Expect(login).ToBeInViewportAsync(new() { Ratio = 1 });
    }

    [Fact]
    public async Task KeyboardFocusStaysInTheMenuAndReturnsToItsTrigger()
    {
        await OpenPageAsync("en-us", "/", 390, 844);
        var toggle = Page.Locator("[data-public-menu-toggle]");
        await toggle.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        var menu = Page.GetByRole(AriaRole.Dialog, new() { Name = "Menu", Exact = true });
        var close = menu.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true });
        await Expect(close).ToBeFocusedAsync();

        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(menu.GetByRole(AriaRole.Link).Last).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(close).ToBeFocusedAsync();

        // A visible background link must not steal focus while the menu is modal.
        await Page.Locator(".public-nav-brand").FocusAsync();
        await Expect(close).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(menu).ToBeHiddenAsync();
        await Expect(toggle).ToBeFocusedAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Theory]
    [InlineData("close")]
    [InlineData("backdrop")]
    [InlineData("escape")]
    public async Task DismissalRestoresPageScrollingAndPreservesScrollPosition(string dismissal)
    {
        await OpenPageAsync("en-us", "/", 390, 844);
        await Page.EvaluateAsync("() => window.scrollTo(0, 500)");
        var toggle = Page.Locator("[data-public-menu-toggle]");
        var scrollBefore = await Page.EvaluateAsync<double>("() => scrollY");
        await toggle.TapAsync();
        var menu = Page.Locator("[data-public-menu]");
        await Expect(menu).ToBeVisibleAsync();
        await Expect(Page.Locator("body")).ToHaveCSSAsync("overflow", "hidden");

        if (dismissal == "close")
            await menu.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).TapAsync();
        else if (dismissal == "backdrop")
            await Page.Touchscreen.TapAsync(4, 400);
        else
            await Page.Keyboard.PressAsync("Escape");

        await Expect(menu).ToBeHiddenAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(toggle).ToBeFocusedAsync();
        Assert.NotEqual("hidden", await Page.Locator("body").EvaluateAsync<string>("body => getComputedStyle(body).overflow"));
        Assert.InRange(await Page.EvaluateAsync<double>("() => scrollY"), scrollBefore - 1, scrollBefore + 1);
    }

    [Fact]
    public async Task ResizingToDesktopClosesTheMenuAndAllowsItToReopenOnMobile()
    {
        await OpenPageAsync("en-us", "/about", 390, 844);
        var toggle = Page.Locator("[data-public-menu-toggle]");
        var menu = Page.Locator("[data-public-menu]");
        await toggle.TapAsync();
        await Expect(menu).ToBeVisibleAsync();

        await Page.SetViewportSizeAsync(1024, 900);
        await Expect(menu).ToBeHiddenAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.NotEqual("hidden", await Page.Locator("body").EvaluateAsync<string>("body => getComputedStyle(body).overflow"));
        await Expect(Page.Locator("#navbarSupportedContent").GetByRole(AriaRole.Link, new() { Name = "Contact", Exact = false })).ToBeVisibleAsync();

        await Page.SetViewportSizeAsync(390, 844);
        await toggle.TapAsync();
        await Expect(menu).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(menu).ToBeHiddenAsync();
    }

    [Fact]
    public async Task NavigationPreservesDutchCultureAndReturningDoesNotLeaveThePageLocked()
    {
        await OpenPageAsync("nl-nl", "/about", 390, 844);
        var toggle = Page.Locator("[data-public-menu-toggle]");
        var menu = Page.Locator("[data-public-menu]");
        await toggle.TapAsync();
        await menu.Locator("a[href='/nl-nl/nt2-integration']").TapAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/nl-nl/nt2-integration");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "nl-NL");
        await Expect(menu).ToBeHiddenAsync();

        await Page.GoBackAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/nl-nl/about");
        await Expect(menu).ToBeHiddenAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.NotEqual("hidden", await Page.Locator("body").EvaluateAsync<string>("body => getComputedStyle(body).overflow"));
        await toggle.TapAsync();
        await Expect(menu).ToBeVisibleAsync();
    }

    private async Task OpenPageAsync(string culture, string path, int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}{path}",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(200, response?.Status);
        await Expect(Page.Locator("[data-public-menu-toggle]")).ToBeEnabledAsync();
    }
}
