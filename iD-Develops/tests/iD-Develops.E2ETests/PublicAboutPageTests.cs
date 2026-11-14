using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PublicAboutPageTests : DiagnosticPageTest
{
    private static readonly string[] CopyKeys =
    [
        "Title", "HeroTitle", "HeroDescription", "OriginEyebrow", "OriginParagraph1", "OriginParagraph2", "OriginParagraph3",
        "DominiqueHeading", "PatrickHeading", "TogetherEyebrow", "TogetherTitle", "TogetherDescription",
        "ValuesEyebrow", "ValuesParagraph1", "ValuesParagraph2", "ScheduleConversation", "ContactButton"
    ];

    [Theory]
    [InlineData("en-us", 390)]
    [InlineData("nl-nl", 320)]
    [InlineData("en-us", 768)]
    [InlineData("nl-nl", 768)]
    [InlineData("en-us", 1440)]
    [InlineData("nl-nl", 1440)]
    public async Task AboutPreservesCompanyCopyReadableProfilesAndWorkingContactActions(string culture, int width)
    {
        await Page.SetViewportSizeAsync(width, 1000);
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        var mode = await ReadModeAsync();
        var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/about",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(200, response!.Status);

        var manager = Resources();
        string Copy(string key) => manager.GetString(key, CultureInfo.GetCultureInfo(culture))!;
        var main = Page.GetByRole(AriaRole.Main);
        var heading = main.GetByRole(AriaRole.Heading, new() { Level = 1 });
        await Expect(heading).ToHaveCountAsync(1);
        await Expect(heading).ToHaveTextAsync(Copy("Title"));
        var rendered = Normalize(await main.TextContentAsync() ?? string.Empty);
        foreach (var key in CopyKeys)
            if (!string.IsNullOrWhiteSpace(Copy(key))) Assert.Contains(Normalize(Copy(key)), rendered, StringComparison.Ordinal);

        var hero = Page.GetByTestId("about-hero");
        await Expect(hero.GetByTestId("about-prose").Locator("p")).ToHaveCountAsync(culture == "en-us" ? 3 : 2);
        var photo = hero.GetByRole(AriaRole.Img, new() { Name = Copy("HeroImageAlt"), Exact = true });
        await Expect(photo).ToHaveJSPropertyAsync("complete", true);
        Assert.True(await photo.EvaluateAsync<bool>("image => image.naturalWidth > 0 && image.currentSrc.endsWith('.webp')"));
        await Expect(photo).ToHaveAttributeAsync("width", "880");
        await Expect(photo).ToHaveAttributeAsync("height", "1021");

        var origins = Page.GetByTestId("about-origins");
        foreach (var key in new[] { "DominiqueHeading", "PatrickHeading" })
            await Expect(origins.GetByRole(AriaRole.Heading, new() { Name = Copy(key), Level = 3, Exact = true })).ToBeVisibleAsync();
        await Expect(origins.GetByRole(AriaRole.Article)).ToHaveCountAsync(2);
        var biographies = origins.GetByTestId("about-prose");
        await Expect(biographies.Nth(0).Locator("p")).ToHaveCountAsync(3);
        await Expect(biographies.Nth(1).Locator("p")).ToHaveCountAsync(culture == "en-us" ? 3 : 2);

        var previousBottom = 0f;
        foreach (var chapter in new[] { "about-hero", "about-origins", "about-together", "service-contact" })
        {
            var bounds = await Page.GetByTestId(chapter).BoundingBoxAsync();
            Assert.True(bounds!.Y >= previousBottom - 1, $"{chapter} overlaps the preceding section.");
            previousBottom = bounds.Y + bounds.Height;
        }

        double[]? commonEdges = null;
        foreach (var frame in await Page.Locator("[data-testid='about-frame'], [data-testid='service-frame']").AllAsync())
        {
            var edges = await frame.EvaluateAsync<double[]>("""
                frame => { const box = frame.getBoundingClientRect(), style = getComputedStyle(frame);
                    return [box.left + parseFloat(style.paddingLeft), box.right - parseFloat(style.paddingRight)]; }
                """);
            commonEdges ??= edges;
            Assert.InRange(Math.Abs(edges[0] - commonEdges[0]), 0, 1);
            Assert.InRange(Math.Abs(edges[1] - commonEdges[1]), 0, 1);
        }
        foreach (var prose in await Page.GetByTestId("about-prose").AllAsync())
        {
            var readable = await prose.EvaluateAsync<bool>("""
                prose => { const paragraphs = [...prose.querySelectorAll('p')];
                    return paragraphs.every((paragraph, index) => {
                        const style = getComputedStyle(paragraph), box = paragraph.getBoundingClientRect();
                        const previous = index ? paragraphs[index - 1].getBoundingClientRect() : null;
                        return parseFloat(style.fontSize) >= 18
                            && Math.abs(parseFloat(style.lineHeight) / parseFloat(style.fontSize) - 1.625) < 0.03
                            && (!previous || box.top - previous.bottom >= 23);
                    }); }
                """);
            Assert.True(readable, "Biography and introduction paragraphs must keep readable type and visible reading breaks.");
        }
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"), $"About overflows at {width}px.");
        var titleBox = await heading.BoundingBoxAsync();
        var navBox = await Page.Locator("header.public-header").BoundingBoxAsync();
        Assert.True(titleBox!.Y >= navBox!.Y + navBox.Height, "The public header overlaps the title.");

        var closing = Page.GetByTestId("service-contact");
        var book = closing.GetByRole(AriaRole.Link, new() { Name = Copy("ScheduleConversation"), Exact = true });
        var contact = closing.GetByRole(AriaRole.Link, new() { Name = Copy("ContactButton"), Exact = true });
        await Expect(book).ToHaveAttributeAsync("href", "https://koalendar.com/e/meet-with-id");
        await Expect(book).ToHaveAttributeAsync("rel", "noopener noreferrer");
        await Expect(contact).ToHaveAttributeAsync("href", $"/{culture}/contact");
        await book.FocusAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(contact).ToBeFocusedAsync();

        await Expect(Page.Locator(".public-nav-login")).ToHaveCountAsync(mode == "Marketing" ? 0 : 2);
        if (width < 1024)
        {
            var toggle = Page.Locator("[data-public-menu-toggle]");
            await Expect(toggle).ToBeEnabledAsync();
            await toggle.ClickAsync();
            var menu = Page.Locator("[data-public-menu]");
            await Expect(menu).ToBeVisibleAsync();
            await Expect(menu.Locator(".public-nav-login")).ToHaveCountAsync(mode == "Marketing" ? 0 : 1);
            await Page.Locator("[data-public-menu-close]").ClickAsync();
            await Expect(menu).ToBeHiddenAsync();
        }
        await Page.ScreenshotAsync(new()
        {
            Path = Path.Combine(AppContext.BaseDirectory, "test-results", $"about-{mode}-{culture}-{width}.png"),
            FullPage = true
        });
        await contact.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
    }

    [Theory]
    [InlineData("en-us")]
    [InlineData("nl-nl")]
    public async Task AboutKeepsContentAndContactNavigationWithoutJavaScript(string culture)
    {
        await using var context = await Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await context.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        foreach (var stylesheet in new[] { "https://fonts.googleapis.com/**", "https://cdnjs.cloudflare.com/**" })
            await context.RouteAsync(stylesheet, route => route.FulfillAsync(new() { ContentType = "text/css", Body = string.Empty }));
        await context.RouteAsync("https://fonts.gstatic.com/**", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/about");
        Assert.Equal(200, response!.Status);
        var manager = Resources();
        var localized = CultureInfo.GetCultureInfo(culture);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = manager.GetString("Title", localized), Level = 1, Exact = true })).ToBeVisibleAsync();
        await page.GetByTestId("service-contact").GetByRole(AriaRole.Link, new() { Name = manager.GetString("ContactButton", localized), Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
    }

    private async Task<string> ReadModeAsync()
    {
        var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/health/version");
        Assert.Equal(200, response!.Status);
        return (await response.JsonAsync())!.Value.GetProperty("siteMode").GetString()!;
    }

    private static ResourceManager Resources() => new("iD_Develops.Resources.Pages.AboutModel", typeof(global::iD_Develops.Pages.AboutModel).Assembly);
    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
