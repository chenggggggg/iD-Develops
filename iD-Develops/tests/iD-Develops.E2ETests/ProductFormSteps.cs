using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

internal static class ProductFormSteps
{
    public static async Task PersonalAsync(IPage page, string? name = null, string? email = null)
    {
        await page.Locator("[data-signup-step='0'] [data-signup-go='1']").ClickAsync();
        if (name != null) await page.Locator("[name='Signup.FullName']").FillAsync(name);
        if (email != null) await page.Locator("[name='Signup.Email']").FillAsync(email);
    }

    public static Task QuestionsAsync(IPage page) => page.Locator("[data-signup-continue]").ClickAsync();
}
