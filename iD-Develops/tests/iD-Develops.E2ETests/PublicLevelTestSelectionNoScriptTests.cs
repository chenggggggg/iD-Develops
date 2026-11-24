using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

[Collection("Public level-test configuration")]
public sealed class PublicLevelTestSelectionNoScriptTests : DiagnosticPageTest
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
        // Native navigation depends on the application's own CSS, not font/icon CDN
        // delivery. Pending external CSS can block rendering with JavaScript disabled.
        // Use the managed context so diagnostics trace the page actually under test.
        await Context.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
        foreach (var stylesheet in new[] { "https://fonts.googleapis.com/**", "https://cdnjs.cloudflare.com/**" })
            await Context.RouteAsync(stylesheet, route => route.FulfillAsync(new() { ContentType = "text/css", Body = string.Empty }));
        await Context.RouteAsync("https://fonts.gstatic.com/**", route => route.AbortAsync());
    }

    [Theory]
    [InlineData("en-us", 390)]
    [InlineData("en-us", 1440)]
    [InlineData("nl-nl", 390)]
    [InlineData("nl-nl", 1440)]
    public async Task ChoicesAndIntroductionsRemainUsableWithoutJavaScript(string culture, int width)
    {
        await using var fixture = await PublicLevelTestSelectionTests.SelectionFixture.CreateAsync();
        await Page.SetViewportSizeAsync(width, 900);
        var overview = $"{LocalTestApplication.PublicBaseUrl}/{culture}/level-test";
        foreach (var level in new[] { "a1", "a2" })
        {
            var link = Page.Locator($"[data-level-test-choice='{level}']").GetByRole(AriaRole.Link);
            var destination = $"{overview}/{level}";
            foreach (var keyboard in new[] { false, true })
            {
                // Wait for first-party styles before relying on stable rendered controls.
                await Page.GotoAsync(overview, new() { WaitUntil = WaitUntilState.Load });
                await Expect(link).ToBeVisibleAsync();
                await Expect(link).ToHaveAttributeAsync("href", $"/{culture}/level-test/{level}");
                var bounds = await link.BoundingBoxAsync();
                Assert.True(bounds!.Width >= 44 && bounds.Height >= 44);
                if (keyboard)
                {
                    await link.FocusAsync();
                    await Page.Keyboard.PressAsync("Enter");
                }
                else
                    await link.ClickAsync();
                await Page.WaitForURLAsync(destination, new() { WaitUntil = WaitUntilState.Load });
                await Expect(Page.Locator("[data-selected-level]")).ToHaveAttributeAsync("data-selected-level", level);
                await Expect(Page.GetByRole(AriaRole.Heading, new()
                {
                    Level = 1,
                    Name = culture == "nl-nl" ? $"Niveautest {level.ToUpperInvariant()}" : $"{level.ToUpperInvariant()} level test",
                    Exact = true
                })).ToBeVisibleAsync();
                await Expect(Page.Locator("[data-test-introduction]")).ToHaveTextAsync(
                    $"{(culture == "nl-nl" ? "Nederlandse" : "English")} introduction {level}");
                await Expect(Page.GetByRole(AriaRole.Button, new()
                {
                    Name = culture == "nl-nl" ? $"Start {level.ToUpperInvariant()}-test" : $"Start {level.ToUpperInvariant()} test",
                    Exact = true
                })).ToBeVisibleAsync();
                Assert.Equal(0, await fixture.RecordCountAsync(fixture.Exams.Single(exam => exam.PublicSlug == Utilities.PublicLevelTests.SlugForLevel(level)).Id));
            }
        }
        var ids = fixture.Exams.Select(exam => exam.Id).ToList();
        Assert.False(await fixture.Db.ExamVersions.AnyAsync(version => ids.Contains(version.ExamId)));
    }
}
