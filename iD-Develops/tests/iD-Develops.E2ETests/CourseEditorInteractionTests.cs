namespace iD_Develops.E2ETests;

public sealed class CourseEditorInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task AddContentDisclosure_RevealsAllTypesAndAddsSelectedItem()
    {
        await Page.SetContentAsync(
            """
            <!doctype html>
            <html>
            <body>
                <script id="courseEditorData" type="application/json">
                {
                    "CourseId": 42,
                    "Name": "Test course",
                    "CreditProducts": [],
                    "ExamOptions": [
                        { "Id": 91, "Name": "Placement exam", "PublishStatus": 1 }
                    ],
                    "Sections": [
                        {
                            "Id": 7,
                            "Title": "Foundations",
                            "OrderNumber": 0,
                            "UnlockAfterValue": null,
                            "UnlockAfterUnit": 1,
                            "Lectures": [],
                            "Assignments": [],
                            "Classes": [],
                            "Exams": []
                        }
                    ]
                }
                </script>
                <button type="button" data-course-add-top="section">New section</button>
                <nav data-course-editor-outline></nav>
                <main data-course-editor-main></main>
                <form id="course-edit-form">
                    <input type="hidden" data-course-editor-json />
                </form>
            </body>
            </html>
            """);

        var scriptPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "wwwroot",
            "js",
            "course-editor.js"));
        await Page.AddScriptTagAsync(new() { Path = scriptPath });

        var toggle = Page.Locator("[data-add-content-toggle]");
        var menu = Page.Locator("[data-add-content-menu]");
        await Expect(toggle).ToBeVisibleAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(menu).ToBeHiddenAsync();

        await toggle.ClickAsync();

        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(menu).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-add-child='lecture']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-add-child='assignment']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-add-child='class']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-add-child='exam']")).ToBeVisibleAsync();

        await Page.Locator("[data-add-child='lecture']").ClickAsync();

        await Expect(Page.Locator("[data-editor-item][data-type='lecture']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-editor-item][data-type='lecture'] input")).ToHaveValueAsync("Untitled lecture");
        await Expect(Page.Locator("[data-add-content-menu]")).ToBeHiddenAsync();
    }
}
