using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class CourseFileUploadInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    private const string EditorUrl = "http://course-uploads.test/courses/42?edit=true";
    private readonly List<Dictionary<string, string>> _prepared = [];

    [Theory]
    [InlineData("lecture", false)]
    [InlineData("assignment", false)]
    [InlineData("lecture", true)]
    [InlineData("assignment", true)]
    public async Task FileSelection_DirtyOrNewContent_RetainsTargetAndWaitsForUploadBeforeSaving(string kind, bool newContent)
    {
        var releaseUpload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await SetupAsync(async route =>
        {
            Assert.Equal("application/pdf", route.Request.Headers["content-type"]);
            uploadStarted.TrySetResult();
            await releaseUpload.Task;
            await route.FulfillAsync(new() { Status = 200 });
        });
        await SelectContentAsync(kind, newContent);
        await Page.GetByLabel("Description", new() { Exact = true }).FillAsync("An unsaved description");
        await ChooseFileAsync();
        try
        {
            await uploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var request = Assert.Single(_prepared);
            Assert.Equal(kind, request["itemType"]);
            Assert.Equal(newContent, int.Parse(request["itemId"]) < 0);
            if (kind == "lecture") Assert.Equal("2", request["lectureContentType"]);
            await Expect(Page.Locator("[data-file-upload-list]")).ToContainTextAsync("Workbook.pdf:");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
            Assert.Equal(0, await Page.EvaluateAsync<int>("window.courseSubmissions.length"));
            await Expect(Page.Locator("[data-course-upload-feedback]")).ToContainTextAsync("Wait for them to finish");

            var otherKind = kind == "lecture" ? "assignment" : "lecture";
            await Page.GetByRole(AriaRole.Textbox, new() { Name = $"{otherKind} title", Exact = true }).First.FocusAsync();
            releaseUpload.TrySetResult();
            await Expect(Page.Locator("[data-course-upload-feedback]")).ToHaveTextAsync(string.Empty);
            await Expect(Page.GetByText("Workbook.pdf", new() { Exact = true })).ToHaveCountAsync(0);
            await Page.GetByRole(AriaRole.Textbox, new() { Name = $"{kind} title", Exact = true }).Nth(newContent ? 1 : 0).FocusAsync();
            await Expect(Page.GetByText("Workbook.pdf", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.GetByText("Ready to save", new() { Exact = true })).ToBeVisibleAsync();

            var submitted = await SaveAsync();
            var section = submitted.GetProperty("Sections")[0];
            var items = section.GetProperty(kind == "lecture" ? "Lectures" : "Assignments");
            var target = items.EnumerateArray().Single(item => item.GetProperty("Id").GetInt32() == int.Parse(request["itemId"]));
            Assert.Equal("An unsaved description", target.GetProperty("Description").GetString());
            var file = Assert.Single(target.GetProperty(kind == "lecture" ? "SourceFiles" : "SupportingFiles").EnumerateArray());
            Assert.Equal("Workbook.pdf", file.GetProperty("Name").GetString());
            Assert.Equal("courses/files/42/Workbook.pdf", file.GetProperty("FileReference").GetString());
            Assert.Equal(0, file.GetProperty("Id").GetInt32());
        }
        finally
        {
            releaseUpload.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedUpload_BlocksSaveAndRetriesWithoutReselectingFile(bool preparationFails)
    {
        var fail = true;
        await SetupAsync(route => route.FulfillAsync(new() { Status = fail && !preparationFails ? 500 : 200 }),
            preparationFails ? async route =>
            {
                if (fail)
                    await route.FulfillAsync(new() { Status = 400, ContentType = "application/json", Body = """{"errorMessage":"Storage temporarily unavailable."}""" });
                else
                    await PrepareSuccessfullyAsync(route);
            }
        : null);
        await SelectContentAsync("assignment", false);
        await ChooseFileAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Workbook.pdf");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        Assert.Equal(0, await Page.EvaluateAsync<int>("window.courseSubmissions.length"));
        await Expect(Page.Locator("[data-course-upload-feedback]")).ToContainTextAsync("Retry or remove");

        fail = false;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry upload", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Ready to save", new() { Exact = true })).ToBeVisibleAsync();
        var submitted = await SaveAsync();
        var files = submitted.GetProperty("Sections")[0].GetProperty("Assignments")[0].GetProperty("SupportingFiles");
        Assert.Single(files.EnumerateArray());
    }

    [Fact]
    public async Task MultipleFiles_AreAllRetainedAndCanBeRemovedIndividually()
    {
        await SetupAsync(route => route.FulfillAsync(new() { Status = 200 }));
        await SelectContentAsync("assignment", true);
        await Page.Locator("[data-upload-file]").SetInputFilesAsync(new[]
        {
            new FilePayload { Name = "First.pdf", MimeType = "application/pdf", Buffer = Encoding.UTF8.GetBytes("First file") },
            new FilePayload { Name = "Second.pdf", MimeType = "application/pdf", Buffer = Encoding.UTF8.GetBytes("Second file") }
        });
        await Expect(Page.GetByText("Ready to save", new() { Exact = true })).ToHaveCountAsync(2);
        var submitted = await SaveAsync();
        var assignment = submitted.GetProperty("Sections")[0].GetProperty("Assignments")[1];
        var names = assignment.GetProperty("SupportingFiles").EnumerateArray().Select(file => file.GetProperty("Name").GetString()).ToArray();
        Assert.Contains("First.pdf", names);
        Assert.Contains("Second.pdf", names);

        await Page.Locator(".course-editor-resource").Filter(new() { HasText = "First.pdf" })
            .GetByRole(AriaRole.Button, new() { Name = "Remove file", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        var updated = await Page.EvaluateAsync<JsonElement>("window.courseSubmissions[1]");
        var remaining = Assert.Single(updated.GetProperty("Sections")[0].GetProperty("Assignments")[1].GetProperty("SupportingFiles").EnumerateArray());
        Assert.Equal("Second.pdf", remaining.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task FailedUpload_CanBeRemovedWithoutSavingABrokenAttachment()
    {
        await SetupAsync(route => route.FulfillAsync(new() { Status = 500 }));
        await SelectContentAsync("assignment", false);
        await ChooseFileAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Workbook.pdf");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove upload", Exact = true }).ClickAsync();

        var submitted = await SaveAsync();
        Assert.Empty(submitted.GetProperty("Sections")[0].GetProperty("Assignments")[0].GetProperty("SupportingFiles").EnumerateArray());
    }

    private async Task SetupAsync(Func<IRoute, Task> put, Func<IRoute, Task>? prepare = null)
    {
        await Page.RouteAsync("http://course-uploads.test/files/upload", put);
        await Page.RouteAsync("http://course-uploads.test/courses/42?handler=CreateFileUpload&courseId=42", prepare ?? PrepareSuccessfullyAsync);
        await Page.RouteAsync(EditorUrl, route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = """
                <!doctype html><html><body>
                    <script id="courseEditorData" type="application/json">
                    {"CourseId":42,"Name":"Attachment course","Sections":[{"Id":7,"Title":"Section","OrderNumber":0,"Lectures":[{"Id":11,"Title":"Existing lecture","ContentType":0,"SourceFiles":[]}],"Assignments":[{"Id":21,"Title":"Existing assignment","SupportingFiles":[]}],"Classes":[],"Exams":[]}]}
                    </script>
                    <nav data-course-editor-outline></nav>
                    <form id="course-edit-form">
                        <input type="hidden" name="__RequestVerificationToken" value="test-token" />
                        <input type="hidden" data-course-editor-json />
                        <main data-course-editor-main></main>
                        <button type="submit">Save</button>
                    </form>
                </body></html>
                """
        }));
        await Page.GotoAsync(EditorUrl);
        await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("course-editor.js") });
        await Page.EvaluateAsync("""
            window.courseSubmissions = [];
            document.getElementById('course-edit-form').addEventListener('submit', event => {
                if (!event.defaultPrevented) window.courseSubmissions.push(JSON.parse(document.querySelector('[data-course-editor-json]').value));
                event.preventDefault();
            });
            """);
    }

    private Task PrepareSuccessfullyAsync(IRoute route)
    {
        var form = QueryHelpers.ParseQuery(route.Request.PostData ?? string.Empty).ToDictionary(item => item.Key, item => item.Value.ToString());
        _prepared.Add(form);
        return route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                objectKey = $"courses/files/42/{form["fileName"]}",
                putUrl = "http://course-uploads.test/files/upload",
                contentType = "application/pdf",
                maxBytes = 1000000
            })
        });
    }

    private async Task SelectContentAsync(string kind, bool newContent)
    {
        if (newContent)
        {
            await Page.Locator("[data-add-content-toggle]").ClickAsync();
            await Page.Locator($"[data-add-child='{kind}']").ClickAsync();
        }
        else
            await Page.GetByRole(AriaRole.Textbox, new() { Name = $"{kind} title", Exact = true }).FocusAsync();
        if (kind == "lecture")
        {
            await Page.Locator("[data-course-editor-main]").GetByRole(AriaRole.Button, new() { Name = "Add content", Exact = true }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Article", Exact = true }).ClickAsync();
        }
    }

    private Task ChooseFileAsync() => Page.Locator("[data-upload-file]").SetInputFilesAsync(new FilePayload
    {
        Name = "Workbook.pdf",
        MimeType = "application/pdf",
        Buffer = Encoding.UTF8.GetBytes("%PDF-1.4 test attachment")
    });

    private async Task<JsonElement> SaveAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        Assert.Equal(1, await Page.EvaluateAsync<int>("window.courseSubmissions.length"));
        return await Page.EvaluateAsync<JsonElement>("window.courseSubmissions[0]");
    }
}
