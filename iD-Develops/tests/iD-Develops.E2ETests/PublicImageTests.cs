using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PublicImageTests : DiagnosticPageTest
{
    [Theory]
    [InlineData("en-us", 390)]
    [InlineData("nl-nl", 390)]
    [InlineData("en-us", 1440)]
    [InlineData("nl-nl", 1440)]
    public async Task HomeTeamUsesSmallResponsivePortraitsWithoutChangingProfileNavigation(string culture, int width)
    {
        await Page.SetViewportSizeAsync(width, 1000);
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.EvaluateAsync("() => document.fonts.ready");
        var carousel = Page.Locator("[data-team-carousel]");
        await carousel.ScrollIntoViewIfNeededAsync();
        var profiles = carousel.Locator("[data-team-profile]");
        await Expect(profiles).ToHaveCountAsync(2);
        var imageSources = new HashSet<string>();
        for (var index = 0; index < 2; index++)
        {
            var tab = carousel.GetByRole(AriaRole.Tab).Nth(index);
            await tab.FocusAsync();
            await Page.Keyboard.PressAsync("Enter");
            await Expect(tab).ToHaveAttributeAsync("aria-selected", "true");
            await Expect(profiles.Nth(index)).ToHaveAttributeAsync("aria-hidden", "false");
            await Expect(profiles.Nth(index).GetByRole(AriaRole.Heading, new() { Level = 3 })).ToBeInViewportAsync();
            var image = profiles.Nth(index).GetByRole(AriaRole.Img);
            await image.EvaluateAsync("image => image.decode()");
            var source = await image.EvaluateAsync<string>("image => image.currentSrc");
            imageSources.Add(source);
            Assert.Contains(index == 0 ? "/images/about/dominique-portrait-" : "/images/about/founders-", source, StringComparison.Ordinal);
            var bytes = await Page.EvaluateAsync<int>("async url => (await (await fetch(url, {cache:'no-store'})).arrayBuffer()).byteLength", source);
            Assert.InRange(bytes, 1, 65000);
            Assert.InRange(await image.EvaluateAsync<int>("image => image.naturalWidth"), 1, 880);
            await Expect(image).ToHaveAttributeAsync("loading", "lazy");
            await Expect(image).ToHaveAttributeAsync("width", "880");
            await Expect(image).ToHaveAttributeAsync("height", "1021");
            await Expect(profiles.Nth(index).GetByRole(AriaRole.Link)).ToHaveAttributeAsync("href", $"/{culture}/about");
        }
        Assert.Equal(2, imageSources.Count);
        Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(false, 390)]
    [InlineData(false, 1440)]
    [InlineData(true, 390)]
    [InlineData(true, 1440)]
    public async Task ErrorIllustrationsLoadWithoutChangingStatusesOrRecoveryLinks(bool portal, int width)
    {
        await Page.SetViewportSizeAsync(width, 1000);
        var baseUrl = portal ? LocalTestApplication.PortalBaseUrl : $"{LocalTestApplication.PublicBaseUrl}/nl-nl";
        foreach (var (code, status) in new[] { ("db-offline", 503), ("bad-request", 400), ("unauthorized", 401), ("forbidden", 403), ("not-found", 404), ("server-error", 503) })
        {
            var response = await Page.GotoAsync($"{baseUrl}/error?code={code}&statusCode={status}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.Equal(status, response!.Status);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
            var image = Page.GetByRole(AriaRole.Img);
            await image.EvaluateAsync("image => image.decode()");
            Assert.False(string.IsNullOrWhiteSpace(await image.GetAttributeAsync("alt")));
            var bytes = await image.EvaluateAsync<int>("async image => (await (await fetch(image.currentSrc, {cache:'no-store'})).arrayBuffer()).byteLength");
            Assert.InRange(bytes, 1, 60000);
            await Expect(image).ToHaveAttributeAsync("width", "1080");
            await Expect(image).ToHaveAttributeAsync("height", "1080");
            var actions = Page.Locator(".error-actions").GetByRole(AriaRole.Link);
            await Expect(actions).ToHaveCountAsync(2);
            foreach (var action in await actions.AllAsync())
            {
                await Expect(action).ToBeVisibleAsync();
                Assert.False(string.IsNullOrWhiteSpace(await action.GetAttributeAsync("href")));
            }
            Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        }
    }

    [Fact]
    public async Task SlideshowFallbackIsSmallAndDecodesInTheBrowser()
    {
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/health/version");
        var result = await Page.EvaluateAsync<int[]>("""
            async url => {
                const response = await fetch(url, {cache:'no-store'});
                const bytes = (await response.arrayBuffer()).byteLength;
                const image = new Image(); image.src = url; await image.decode();
                return [response.status, bytes, image.naturalWidth, image.naturalHeight];
            }
            """, $"{LocalTestApplication.PublicBaseUrl}/images/gradient.webp");
        Assert.Equal(200, result[0]);
        Assert.InRange(result[1], 1, 125000);
        Assert.InRange(result[2], 1, 1920);
        Assert.InRange((double)result[2] / result[3], 1.49, 1.51);
    }
}
