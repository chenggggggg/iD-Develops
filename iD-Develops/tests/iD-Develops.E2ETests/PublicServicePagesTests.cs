using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PublicServicePagesTests : DiagnosticPageTest
{
    private static readonly (string Route, string Resource, string[] Keys)[] Services =
    [
        ("enterprise", "EnterpriseModel",
        [
            "HeroEyebrow", "HeroTitle", "HeroDescription", "ScheduleConversation", "ContactButton",
            "ChallengesEyebrow", "Challenge1", "Challenge2", "Challenge3", "Challenge4", "Challenge5", "ChallengesConclusion",
            "ServicesEyebrow", "ServiceNt2Title", "ServiceNt2Description", "ServiceCommunicationTitle", "ServiceCommunicationDescription",
            "ServiceDevelopmentTitle", "ServiceDevelopmentDescription", "ServiceProgramsTitle", "ServiceProgramsDescription",
            "ServiceTrainerTitle", "ServiceTrainerDescription", "MatchTitle", "MatchDescription1", "MatchDescription2", "MatchButton"
        ]),
        ("nt2-integration", "Nt2IntegrationModel",
        [
            "Title", "HeroTitle", "HeroDescription", "LevelTestButton", "ConversationButton", "HeroNoteTitle", "HeroNoteDescription",
            "PromiseParagraph", "WhyUsEyebrow", "WhyUsParagraph1", "WhyUsParagraph2", "WhyUsParagraph3",
            "TailoredEyebrow", "TailoredTitle", "TailoredParagraph1", "TailoredParagraph2", "TailoredParagraph3",
            "OfferEyebrow", "OfferRegularTitle", "OfferRegularDescription", "OfferDiyTitle", "OfferDiyDescription", "ContactButton", "FreeConversationButton"
        ]),
        ("personal-development", "Personal_DevelopmentModel",
        [
            "Title", "HeroTitle", "HeroDescription", "ScheduleConversation", "ContactButton", "HeroNoteTitle", "HeroNoteDescription",
            "ApproachEyebrow", "ApproachParagraph1", "ApproachParagraph2", "AudienceEyebrow", "AudienceTitle", "AudienceDescription",
            "HelpEyebrow", "HelpItem1", "HelpItem2", "HelpItem3", "HelpItem4", "HelpItem5", "HelpItem6", "HelpItem7", "HelpItem8",
            "MethodsEyebrow", "Method1Title", "Method1Description", "Method2Title", "Method2Description", "Method3Title", "Method3Description",
            "Method4Title", "Method4Description", "MethodsConclusion", "MethodsButton"
        ]),
        ("language-and-communication", "LanguageAndCommunication.IndexModel",
        [
            "Title", "HeroTitle", "HeroParagraph1", "HeroParagraph2", "ConversationButton", "ContactButton", "ServicesEyebrow",
            "ServiceEnglishTitle", "ServiceEnglishDescription", "ServiceInterculturalTitle", "ServiceInterculturalDescription",
            "ServiceSpeakingTitle", "ServiceSpeakingDescription", "ServiceWritingTitle", "ServiceWritingDescription", "ServiceOutsourceTitle", "ServiceOutsourceDescription",
            "AudienceEyebrow", "AudienceTitle", "AudienceParagraph1", "AudienceParagraph2", "AudienceParagraph3"
        ])
    ];

    [Theory]
    [InlineData("en-us", 390)]
    [InlineData("nl-nl", 320)]
    [InlineData("en-us", 768)]
    [InlineData("nl-nl", 768)]
    [InlineData("en-us", 1440)]
    [InlineData("nl-nl", 1440)]
    public async Task ServicesPreserveLocalizedContentAndWorkingActionsWithoutOverflow(string culture, int width)
    {
        await Page.SetViewportSizeAsync(width, 1000);
        // Third-party consent must not obscure or delay first-party layout/navigation checks.
        await Page.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        foreach (var (route, resource, keys) in Services)
        {
            var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/{route}",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.Equal(200, response!.Status);
            var hero = Page.GetByTestId("service-hero");
            await Expect(hero.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveCountAsync(1);

            var manager = new ResourceManager($"iD_Develops.Resources.Pages.{resource}", typeof(global::iD_Develops.Pages.EnterpriseModel).Assembly);
            // textContent preserves localized casing; eyebrow styling deliberately renders uppercase.
            var text = Normalize(await Page.GetByRole(AriaRole.Main).TextContentAsync() ?? string.Empty);
            foreach (var key in keys)
                AssertCopy(manager, culture, key, text);
            if (route == "nt2-integration")
            {
                var expanded = bool.Parse(manager.GetString("ShowExpandedOffer", CultureInfo.GetCultureInfo(culture))!);
                await Expect(Page.GetByTestId("service-offerings").Locator("article")).ToHaveCountAsync(2);
                var support = Page.GetByTestId("nt2-additional-support");
                await Expect(support).ToHaveCountAsync(expanded ? 1 : 0);
                if (expanded)
                {
                    await Expect(support.Locator("article")).ToHaveCountAsync(5);
                    await Expect(support.Locator("article").First).ToBeHiddenAsync();
                    await support.Locator("summary").FocusAsync();
                    await Page.Keyboard.PressAsync("Enter");
                    await Expect(support.Locator("article").First).ToBeVisibleAsync();
                    for (var index = 1; index <= 5; index++)
                    {
                        AssertCopy(manager, culture, $"OfferExtra{index}Title", text);
                        AssertCopy(manager, culture, $"OfferExtra{index}Description", text);
                        await Expect(support.GetByRole(AriaRole.Heading, new()
                        {
                            Name = manager.GetString($"OfferExtra{index}Title", CultureInfo.GetCultureInfo(culture)),
                            Exact = true
                        })).ToBeVisibleAsync();
                    }
                    await support.Locator("summary").PressAsync("Enter");
                    await Expect(support.Locator("article").First).ToBeHiddenAsync();
                    AssertCopy(manager, culture, "TestimonialsEyebrow", text);
                    for (var index = 1; index <= 3; index++) AssertCopy(manager, culture, $"Testimonial{index}", text);
                }
                await Page.EvaluateAsync("window.scrollTo({ top: 0, behavior: 'instant' })");
                var previousBottom = 0f;
                foreach (var chapter in new[] { "service-hero", "nt2-goals", "nt2-approach", "nt2-courses", "nt2-trainers", "service-contact" })
                {
                    var bounds = await Page.GetByTestId(chapter).BoundingBoxAsync();
                    Assert.True(bounds!.Y >= previousBottom - 1, $"NT2 chapter {chapter} must follow the preceding chapter.");
                    previousBottom = bounds.Y + bounds.Height;
                }
                var approach = Page.GetByTestId("nt2-approach");
                foreach (var key in new[] { "PracticalDutchHeading", "PersonalGuidanceHeading", "PromiseHeading" })
                    await Expect(approach.GetByRole(AriaRole.Heading, new() { Name = manager.GetString(key, CultureInfo.GetCultureInfo(culture)), Exact = true })).ToBeVisibleAsync();
                await Expect(approach.GetByTestId("service-prose").Locator("p")).ToHaveCountAsync(2);
                await Expect(approach.GetByTestId("service-prose").Locator("p").Nth(1))
                    .ToContainTextAsync(manager.GetString("CultureExamplesStart", CultureInfo.GetCultureInfo(culture))!);
                await Expect(hero.GetByRole(AriaRole.Heading, new() { Level = 2 })).ToHaveCountAsync(0);
                await Expect(approach.Locator("aside, blockquote")).ToHaveCountAsync(0);
                await Expect(Page.Locator("main blockquote")).ToHaveCountAsync(expanded ? 3 : 0);
            }

            if (route is "nt2-integration" or "personal-development")
            {
                var frames = await Page.GetByTestId("service-frame").AllAsync();
                Assert.True(frames.Count >= 4);
                double[]? sharedEdges = null;
                foreach (var frame in frames)
                {
                    var edges = await frame.EvaluateAsync<double[]>("""
                        frame => {
                            const box = frame.getBoundingClientRect(), style = getComputedStyle(frame);
                            const children = Array.from(frame.children, child => child.getBoundingClientRect())
                                .filter(box => box.width > 0 && box.height > 0);
                            return [box.left + parseFloat(style.paddingLeft), box.right - parseFloat(style.paddingRight),
                                Math.min(...children.map(box => box.left)), Math.max(...children.map(box => box.right))];
                        }
                        """);
                    sharedEdges ??= edges;
                    Assert.InRange(Math.Abs(edges[0] - sharedEdges[0]), 0, 1);
                    Assert.InRange(Math.Abs(edges[1] - sharedEdges[1]), 0, 1);
                    Assert.InRange(Math.Abs(edges[2] - edges[0]), 0, 1);
                    Assert.InRange(Math.Abs(edges[3] - edges[1]), 0, 1);
                }
                await Expect(Page.GetByTestId("service-prose").First).ToBeVisibleAsync();
                foreach (var prose in await Page.GetByTestId("service-prose").AllAsync())
                {
                    // Check the rendered text rather than enforcing the old centered-column width.
                    var readable = await prose.EvaluateAsync<bool>("""
                        prose => {
                            const paragraphs = Array.from(prose.querySelectorAll('p'));
                            const boxes = paragraphs.map(p => p.getBoundingClientRect());
                            if (!paragraphs.every((p, i) => {
                                const style = getComputedStyle(p), size = parseFloat(style.fontSize);
                                return size >= 18 && parseFloat(style.lineHeight) >= size * 1.5
                                    && boxes[i].width >= Math.min(innerWidth - 32, 260);
                            })) return false;
                            for (let i = 0; i < boxes.length; i++) for (let j = i + 1; j < boxes.length; j++) {
                                const a = boxes[i], b = boxes[j];
                                const overlapX = Math.min(a.right, b.right) - Math.max(a.left, b.left);
                                const overlapY = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
                                if (overlapX > 1 && overlapY > 1) return false;
                                if (overlapX > 1 && Math.max(a.top, b.top) - Math.min(a.bottom, b.bottom) < 16) return false;
                                if (overlapY > 1 && Math.max(a.left, b.left) - Math.min(a.right, b.right) < 16) return false;
                            }
                            return true;
                        }
                        """);
                    Assert.True(readable, $"{route}: text must remain readable and paragraphs must not collide.");
                }
            }

            var image = hero.Locator("img");
            await Expect(image).ToHaveJSPropertyAsync("complete", true);
            Assert.True(await image.EvaluateAsync<bool>("image => image.naturalWidth > 0"));
            Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"), $"{culture}/{route} overflows at {width}px.");
            var heading = await hero.GetByRole(AriaRole.Heading, new() { Level = 1 }).BoundingBoxAsync();
            var header = await Page.Locator("header.public-header").BoundingBoxAsync();
            Assert.True(heading!.Y >= header!.Y + header.Height, "Navigation overlaps the service heading.");

            var actions = hero.GetByRole(AriaRole.Link);
            await Expect(actions).ToHaveCountAsync(2);
            await actions.First.FocusAsync();
            await Page.Keyboard.PressAsync("Tab");
            await Expect(actions.Nth(1)).ToBeFocusedAsync();
            foreach (var link in await Page.Locator("main a[href*='koalendar.com']").AllAsync())
            {
                await Expect(link).ToHaveAttributeAsync("href", route == "enterprise" ? "https://koalendar.com/e/samenwerken" : "https://koalendar.com/e/meet-with-id");
                await Expect(link).ToHaveAttributeAsync("rel", "noopener noreferrer");
            }
            var contact = Page.Locator($"section a[href='/{culture}/contact']").First;
            await Expect(contact).ToBeVisibleAsync();
            if (route == "nt2-integration")
                await Expect(Page.Locator($"section a[href='/{culture}/level-test']").First).ToBeVisibleAsync();

            await Page.ScreenshotAsync(new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "test-results", $"service-{route}-{culture}-{width}.png"),
                FullPage = true
            });
        }
    }

    [Theory]
    [InlineData("en-us")]
    [InlineData("nl-nl")]
    public async Task ServiceLinksNavigateWithoutJavaScriptOrThirdPartyAssets(string culture)
    {
        await using var context = await Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await context.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        // With JavaScript disabled DOMContentLoaded can fire before external CSS arrives.
        // Keep this fallback-navigation check independent of render-blocking font CDNs.
        // The responsive visual cases above still render with the site's normal fonts.
        foreach (var stylesheet in new[] { "https://fonts.googleapis.com/**", "https://cdnjs.cloudflare.com/**" })
            await context.RouteAsync(stylesheet, route => route.FulfillAsync(new() { ContentType = "text/css", Body = string.Empty }));
        await context.RouteAsync("https://fonts.gstatic.com/**", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        foreach (var (route, _, _) in Services)
        {
            await page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/{route}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(page.GetByTestId("service-hero").GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
            var destination = route == "nt2-integration" ? "level-test" : "contact";
            await page.Locator($"section a[href='/{culture}/{destination}']").First.ClickAsync();
            await Expect(page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/{destination}");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }).First).ToBeVisibleAsync();
        }
    }

    private static void AssertCopy(ResourceManager manager, string culture, string key, string renderedText)
    {
        var value = manager.GetString(key, CultureInfo.GetCultureInfo(culture));
        Assert.NotNull(value);
        if (!string.IsNullOrWhiteSpace(value))
            Assert.Contains(Normalize(value), renderedText, StringComparison.Ordinal);
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
