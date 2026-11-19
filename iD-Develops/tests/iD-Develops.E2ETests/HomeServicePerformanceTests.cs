using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class HomeServicePerformanceTests : DiagnosticPageTest
{
    private static readonly Regex ServiceImages = new(@"/images/(?:services/(?:nt2|personal-development|communication|enterprise)-\d+|nt2-integration|personal-development|language-communication|training-development-for-organizations)\.webp");

    [Theory]
    [InlineData("en-us")]
    [InlineData("nl-nl")]
    public async Task RevealDoesNotRequireCookieConsent(string culture)
    {
        await Page.SetViewportSizeAsync(1440, 1000);
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        const string pendingConsent = "window.Cookiebot = { consent: { hasResponse: false, necessary: true, preferences: false, statistics: false, marketing: false } };";
        // Exercise the same pending-consent contract even when the test server has Cookiebot disabled.
        await Context.AddInitScriptAsync(pendingConsent);
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript",
            Body = pendingConsent
        }));
        // Model automatic consent blocking: only exempt, cookie-free scripts may run before consent.
        var allowedBeforeConsent = false;
        await Page.RouteAsync("**/js/home-reveal.js*", async route =>
        {
            var script = Page.Locator("script[src*='/js/home-reveal.js']");
            allowedBeforeConsent = await script.GetAttributeAsync("data-cookieconsent") == "ignore";
            if (allowedBeforeConsent)
                await route.ContinueAsync();
            else
                await route.AbortAsync();
        });

        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.True(allowedBeforeConsent);
        await Page.WaitForFunctionAsync("() => window.Cookiebot?.consent?.hasResponse === false");
        var group = Page.Locator("[data-services-reveal]");
        await Expect(group).ToHaveCSSAsync("opacity", "0");
        await Page.EvaluateAsync("() => scrollTo({top:document.getElementById('home-services').offsetTop - 80,behavior:'instant'})");
        await Expect(group).ToHaveCSSAsync("opacity", "1");
        Assert.False(await Page.EvaluateAsync<bool>("() => window.Cookiebot.consent.hasResponse"));
    }

    [Theory]
    [InlineData("en-us", 1440)]
    [InlineData("nl-nl", 1440)]
    [InlineData("en-us", 390)]
    [InlineData("nl-nl", 390)]
    public async Task ServiceSectionFadesTogetherWhileSmallImagesLoad(string culture, int width)
    {
        await Page.SetViewportSizeAsync(width, 1000);
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        var releaseImages = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync(ServiceImages, async route => { await releaseImages.Task; await route.ContinueAsync(); });
        try
        {
            var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.Equal(200, response!.Status);
            var section = Page.Locator("#home-services");
            var group = section.Locator("[data-services-reveal]");
            await Expect(group).ToHaveCSSAsync("opacity", "0");
            var transition = "el => { const css = getComputedStyle(el); return [css.transitionProperty, css.transitionDuration, css.transitionTimingFunction]; }";
            Assert.Equal(await Page.Locator("#home-services + section header[data-reveal]").EvaluateAsync<string[]>(transition),
                await group.EvaluateAsync<string[]>(transition));
            Assert.True(await group.EvaluateAsync<bool>("el => [...el.querySelectorAll('h2,article')].every(node => getComputedStyle(node).opacity === '1')"));
            await Page.EvaluateAsync("() => scrollTo({top:document.getElementById('home-services').offsetTop - 80,behavior:'instant'})");
            await Expect(group).ToHaveCSSAsync("opacity", "1");
            var heading = section.GetByRole(AriaRole.Heading, new() { Level = 2 });
            Assert.Equal("1", await heading.EvaluateAsync<string>("el => getComputedStyle(el.parentElement).opacity"));
            await Expect(heading).ToBeVisibleAsync();
            await Expect(section.GetByRole(AriaRole.Article)).ToHaveCountAsync(4);
            var resources = new ResourceManager("iD_Develops.Resources.Pages.IndexModel", typeof(global::iD_Develops.Pages.IndexModel).Assembly);
            foreach (var key in new[] { "ServiceNt2Title", "ServiceDevelopmentTitle", "ServiceCommunicationTitle", "ServiceOrganizationsTitle" })
                await Expect(section.GetByRole(AriaRole.Heading, new() { Name = resources.GetString(key, CultureInfo.GetCultureInfo(culture)), Exact = true })).ToBeVisibleAsync();
            Assert.True(await section.Locator("[data-service-image]").First.EvaluateAsync<bool>("image => !image.complete"));
            Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));

            releaseImages.SetResult();
            foreach (var image in await section.Locator("[data-service-image]").AllAsync())
                await image.ScrollIntoViewIfNeededAsync();
            await Page.WaitForFunctionAsync("""
                () => [...document.querySelectorAll('[data-service-image]')].every(image => image.complete && image.naturalWidth > 0
                    && performance.getEntriesByName(image.currentSrc).some(entry => entry.decodedBodySize > 0))
                """);
            var bytes = await Page.EvaluateAsync<long>("""
                () => [...new Set([...document.querySelectorAll('[data-service-image]')].map(image => image.currentSrc))]
                    .reduce((sum, src) => sum + performance.getEntriesByName(src).at(-1).decodedBodySize, 0)
                """);
            Assert.InRange(bytes, 1, 220000);
            Assert.True(await section.Locator("[data-service-image]").EvaluateAllAsync<bool>("images => images.every(image => image.naturalWidth <= 960)"));
            Assert.Equal("1", await Page.EvaluateAsync<string>("""
                async () => {
                    scrollTo({top:0,behavior:'instant'});
                    await new Promise(requestAnimationFrame);
                    scrollTo({top:document.getElementById('home-services').offsetTop - 80,behavior:'instant'});
                    await new Promise(requestAnimationFrame);
                    return getComputedStyle(document.querySelector('[data-services-reveal]')).opacity;
                }
                """));
        }
        finally
        {
            releaseImages.TrySetResult();
            await Page.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.Wait });
        }
    }

    [Theory]
    [InlineData("en-us")]
    [InlineData("nl-nl")]
    public async Task HoverAvoidsReflowAndKeyboardCanReachEveryService(string culture)
    {
        await Page.SetViewportSizeAsync(1440, 1000);
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var cards = Page.Locator("[data-services-grid] article");
        await cards.First.ScrollIntoViewIfNeededAsync();
        await Page.EvaluateAsync("() => document.fonts.ready");
        await Page.Mouse.MoveAsync(0, 0);
        var measurements = cards.First.EvaluateAsync<double[][]>("""
            async card => {
                const values = [];
                for (let i = 0; i < 28; i++) {
                    await new Promise(requestAnimationFrame);
                    values.push([card.querySelector('h3').offsetTop, card.querySelector('a').offsetTop]);
                }
                return values;
            }
            """);
        await cards.First.HoverAsync();
        var samples = await measurements;
        Assert.All(samples, sample => Assert.Equal(samples[0], sample));

        foreach (var link in await cards.GetByRole(AriaRole.Link).AllAsync())
        {
            await link.FocusAsync();
            await Expect(link).ToBeFocusedAsync();
            await Expect(link).ToHaveCSSAsync("opacity", "1");
            await Expect(link).ToBeInViewportAsync();
            Assert.True(await link.EvaluateAsync<bool>("el => { const card=el.closest('article').getBoundingClientRect(), link=el.getBoundingClientRect(); return link.top >= card.top && link.bottom <= card.bottom; }"));
            await Expect(link).ToHaveAttributeAsync("href", new Regex($"^/{culture}/"));
        }
        var first = cards.First.GetByRole(AriaRole.Link);
        await first.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/nt2-integration");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReducedMotionAndLargeTouchScreensKeepActionsVisible(bool touch)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            HasTouch = touch,
            ReducedMotion = touch ? ReducedMotion.NoPreference : ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        await page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (!touch)
        {
            await Expect(page.Locator("[data-services-reveal]")).ToHaveCSSAsync("opacity", "1");
            Assert.Equal(0, await page.Locator("[data-services-reveal]").EvaluateAsync<int>("el => el.getAnimations().length"));
        }
        await page.Locator("#home-services h2").ScrollIntoViewIfNeededAsync();
        await Expect(page.Locator("[data-services-reveal]")).ToHaveCSSAsync("opacity", "1");
        foreach (var card in await page.Locator("[data-services-grid] article").AllAsync())
        {
            var link = card.GetByRole(AriaRole.Link);
            await Expect(link).ToHaveCSSAsync("opacity", "1");
            await Expect(link).ToBeVisibleAsync();
            Assert.Equal("none", await card.Locator("[data-service-content]").EvaluateAsync<string>("el => getComputedStyle(el).translate"));
        }
    }

    [Fact]
    public async Task KeyboardFocusExposesTheSectionImmediately()
    {
        await Page.SetViewportSizeAsync(1440, 1000);
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var group = Page.Locator("[data-services-reveal]");
        await Expect(group).ToHaveCSSAsync("opacity", "0");
        var link = group.GetByRole(AriaRole.Link).First;
        await link.FocusAsync();
        await Expect(link).ToBeFocusedAsync();
        Assert.Equal("1", await group.EvaluateAsync<string>("el => getComputedStyle(el).opacity"));
        Assert.Equal(0, await group.EvaluateAsync<int>("el => el.getAnimations().length"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableRevealScriptOrObserverKeepsContentVisible(bool disableObserver)
    {
        if (disableObserver)
            await Context.AddInitScriptAsync("delete window.IntersectionObserver;");
        else
            await Page.RouteAsync("**/js/home-reveal.js*", route => route.AbortAsync());
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var group = Page.Locator("[data-services-reveal]");
        await Expect(group).ToHaveCSSAsync("opacity", "1");
        var link = group.GetByRole(AriaRole.Link).First;
        await link.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/nt2-integration");
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task FailedImagesAndDisabledJavaScriptDoNotBlockServiceNavigation(int width)
    {
        await using var context = await Browser.NewContextAsync(new() { JavaScriptEnabled = false, ViewportSize = new() { Width = width, Height = 1000 } });
        var page = await context.NewPageAsync();
        await page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        await page.RouteAsync(ServiceImages, route => route.AbortAsync());
        await page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/nl-nl/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var section = page.Locator("#home-services");
        await section.GetByRole(AriaRole.Heading, new() { Level = 2 }).ScrollIntoViewIfNeededAsync();
        await Expect(section.GetByRole(AriaRole.Heading, new() { Level = 2 })).ToBeVisibleAsync();
        var link = section.GetByRole(AriaRole.Article).Last.GetByRole(AriaRole.Link);
        await link.FocusAsync();
        await Expect(link).ToHaveCSSAsync("opacity", "1");
        await page.Keyboard.PressAsync("Enter");
        await Expect(page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/nl-nl/enterprise");
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task WheelScrollingAndRefreshAtTheSectionShowTheFade(int width)
    {
        await Page.SetViewportSizeAsync(width, 1000);
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        // The shared CSS fade must also work without the programmatic animation API.
        await Context.AddInitScriptAsync("Element.prototype.animate = undefined;");
        await Context.AddInitScriptAsync("""
            window.serviceRevealProbe = { initial: null, started: null };
            document.addEventListener('DOMContentLoaded', () => requestAnimationFrame(() => {
                const group = document.querySelector('[data-services-reveal]');
                if (!group) return;
                window.serviceRevealProbe.initial = {
                    opacity: parseFloat(getComputedStyle(group).opacity)
                };
                const sample = () => {
                    const opacity = parseFloat(getComputedStyle(group).opacity);
                    if (opacity > 0 && opacity < 1) {
                        const heading = group.querySelector('h2').getBoundingClientRect();
                        window.serviceRevealProbe.started = { headingTop: heading.top, viewportHeight: innerHeight };
                    } else {
                        requestAnimationFrame(sample);
                    }
                };
                sample();
            }));
            """);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var group = Page.Locator("[data-services-reveal]");
        await Expect(group).ToHaveCSSAsync("opacity", "0");
        for (var step = 0; step < 25; step++)
        {
            if (await Page.EvaluateAsync<bool>("() => !!window.serviceRevealProbe.started")) break;
            await Page.Mouse.WheelAsync(0, 80);
            // Model separate wheel gestures instead of jumping directly to the section.
            await Page.WaitForTimeoutAsync(150);
        }
        await Page.WaitForFunctionAsync("() => !!window.serviceRevealProbe.started");
        var entry = await Page.EvaluateAsync<double[]>("() => [window.serviceRevealProbe.started.headingTop, window.serviceRevealProbe.started.viewportHeight]");
        Assert.InRange(entry[0], 0, entry[1] - 1);
        await Expect(group).ToHaveCSSAsync("opacity", "1");

        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.WaitForFunctionAsync("() => !!window.serviceRevealProbe.initial");
        Assert.True(await Page.EvaluateAsync<bool>("() => scrollY > 0"));
        Assert.InRange(await Page.EvaluateAsync<double>("() => window.serviceRevealProbe.initial.opacity"), 0, 0.999);
        await Page.WaitForFunctionAsync("() => !!window.serviceRevealProbe.started");
        await Expect(group).ToHaveCSSAsync("opacity", "1");

        // Refresh below the heading, where only the lower cards are on screen.
        await group.GetByRole(AriaRole.Article).Last.GetByRole(AriaRole.Heading, new() { Level = 3 }).ScrollIntoViewIfNeededAsync();
        Assert.True(await Page.Locator("#home-services h2").EvaluateAsync<bool>("el => el.getBoundingClientRect().bottom < 0"));
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.WaitForFunctionAsync("() => !!window.serviceRevealProbe.initial");
        Assert.InRange(await Page.EvaluateAsync<double>("() => window.serviceRevealProbe.initial.opacity"), 0, 0.999);
        await Page.WaitForFunctionAsync("() => !!window.serviceRevealProbe.started");
        await Expect(group).ToHaveCSSAsync("opacity", "1");

        // A large jump can pass the heading between observer callbacks.
        await Page.EvaluateAsync("() => scrollTo({top:0,behavior:'instant'})");
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(group).ToHaveCSSAsync("opacity", "0");
        await group.GetByRole(AriaRole.Article).Last.GetByRole(AriaRole.Heading, new() { Level = 3 }).ScrollIntoViewIfNeededAsync();
        await Expect(group).ToHaveCSSAsync("opacity", "1");
    }

}
