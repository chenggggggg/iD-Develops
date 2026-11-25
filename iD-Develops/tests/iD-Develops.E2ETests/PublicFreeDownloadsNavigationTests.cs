using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PublicFreeDownloadsNavigationTests : DiagnosticPageTest
{
    [Theory]
    [InlineData("en-us", false)]
    [InlineData("en-us", true)]
    [InlineData("nl-nl", false)]
    [InlineData("nl-nl", true)]
    public async Task PricingAndFooterLinkToLocalizedDownloadsInTheRequestedOrder(string culture, bool mobile)
    {
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 900);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/pricing");
        var pricingLink = Page.Locator("[data-pricing-free-downloads]").GetByRole(AriaRole.Link);
        var label = culture == "nl-nl" ? "Gratis downloads" : "Free downloads";
        var footerLabel = culture == "nl-nl" ? "Gratis Downloads" : "Free Downloads";
        await Expect(pricingLink).ToHaveAccessibleNameAsync(label);
        await Expect(pricingLink).ToHaveAttributeAsync("href", $"/{culture}/free-downloads");
        var footerLinks = Page.Locator("[data-footer-services]").GetByRole(AriaRole.Link);
        var expectedPaths = new[] { "nt2-integration", "personal-development", "language-and-communication", "enterprise", "free-downloads", "pricing" };
        await Expect(footerLinks).ToHaveCountAsync(expectedPaths.Length);
        for (var index = 0; index < expectedPaths.Length; index++)
            await Expect(footerLinks.Nth(index)).ToHaveAttributeAsync("href", $"/{culture}/{expectedPaths[index]}");
        await Expect(footerLinks.Nth(4)).ToHaveAccessibleNameAsync(footerLabel);
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await pricingLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/free-downloads");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(culture == "nl-nl" ? "Gratis downloads" : "Free Downloads");
        await Page.Locator("[data-footer-services]").GetByRole(AriaRole.Link).Last.ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/pricing");
        await Page.Locator("[data-footer-services]").GetByRole(AriaRole.Link, new() { Name = footerLabel, Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/free-downloads");
    }
}
