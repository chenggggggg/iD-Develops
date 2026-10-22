using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ExamMediaSaveFeedbackTests : Microsoft.Playwright.Xunit.PageTest
{
    private const string EditorUrl = "http://exam-media.test/exams/5/edit";
    private readonly List<string> _saved = [];
    private int _uploads;
    private int _prepared;
    private int _navigations;

    [Theory]
    [InlineData("image", false)]
    [InlineData("audio", false)]
    [InlineData("image", true)]
    [InlineData("audio", true)]
    public async Task FailedUpload_RemainsMarkedAfterTextSave_AndRetriesWithoutReselecting(string kind, bool preparationFails)
    {
        var fail = true;
        await SetupAsync(
            put: route => route.FulfillAsync(new() { Status = fail && !preparationFails ? 500 : 200 }),
            prepare: preparationFails ? route => fail
                ? route.FulfillAsync(new() { Status = 400, ContentType = "application/json", Body = """{"success":false,"errorMessage":"Storage unavailable."}""" })
                : PreparedAsync(route) : null);
        await ChooseAsync(kind);
        await Expect(Feedback(kind)).ToContainTextAsync("not saved");
        await Expect(Feedback(kind)).Not.ToContainTextAsync("PRESIGN_FAILED");
        await Expect(Feedback(kind)).Not.ToContainTextAsync("PUT to object storage");
        await Page.GetByLabel("Question text").FillAsync("Text changed after the upload failed");
        await Expect(Page.Locator("[data-save-status]")).ToHaveTextAsync("Media not saved");
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Media not saved");
        await Page.WaitForFunctionAsync("() => window.savedCount > 0");
        await Expect(Feedback(kind)).ToContainTextAsync("not saved");
        await Expect(Page.Locator($"#{kind}Input")).ToHaveAttributeAsync("aria-invalid", "true");

        fail = false;
        await RetryAsync(kind);
        await Expect(Feedback(kind)).ToBeHiddenAsync();
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
        Assert.Contains("Text changed after the upload failed", _saved.Last());
        Assert.Contains($"exams/{kind}/replacement", _saved.Last());
    }

    [Theory]
    [InlineData("image")]
    [InlineData("audio")]
    public async Task UploadedFile_ReferenceSaveFailure_RetriesWithoutUploadingAgain(string kind)
    {
        var fail = true;
        await SetupAsync(save: route => fail
            ? route.FulfillAsync(new() { Status = 400, ContentType = "application/json", Body = """{"success":false,"errorMessage":"Reference save failed."}""" })
            : SavedAsync(route));
        await ChooseAsync(kind);
        await Expect(Feedback(kind)).ToContainTextAsync("not saved");
        Assert.Equal(1, _uploads);
        Assert.Equal(1, _prepared);
        fail = false;
        await RetryAsync(kind);
        await Expect(Feedback(kind)).ToBeHiddenAsync();
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
        Assert.Equal(1, _uploads);
        Assert.Equal(1, _prepared);
    }

    [Fact]
    public async Task IndependentFailures_RetryingOneFileDoesNotClearTheOther()
    {
        var fail = true;
        await SetupAsync(put: route => route.FulfillAsync(new() { Status = fail ? 500 : 200 }));
        await ChooseAsync("image");
        await Expect(Feedback("image")).ToContainTextAsync("not saved");
        await ChooseAsync("audio");
        await Expect(Feedback("audio")).ToContainTextAsync("not saved");
        fail = false;
        await RetryAsync("image");
        await Expect(Feedback("image")).ToBeHiddenAsync();
        await Expect(Feedback("audio")).ToContainTextAsync("not saved");
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Media not saved");
        await Page.Locator("#editorSaveRetry").ClickAsync();
        await Expect(Feedback("audio")).ToBeHiddenAsync();
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
    }

    [Theory]
    [InlineData("image")]
    [InlineData("audio")]
    public async Task DiscardFailedReplacement_RestoresOriginalMedia_AndWarnsBeforeLeaving(string kind)
    {
        await SetupAsync(put: route => route.FulfillAsync(new() { Status = 500 }));
        await ChooseAsync(kind);
        await Expect(Feedback(kind)).ToContainTextAsync("not saved");
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => { const event = new Event('beforeunload', {cancelable: true}); window.dispatchEvent(event); return event.defaultPrevented; }
            """));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Question 2", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Media not saved", Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Stay on this question", Exact = true }).ClickAsync();
        Assert.Equal(0, _navigations);
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Discard {kind} upload", Exact = true }).ClickAsync();
        await Expect(Feedback(kind)).ToBeHiddenAsync();
        await Expect(Page.Locator($"input[name='{ReferenceName(kind)}']")).ToHaveValueAsync($"exams/{kind}/original");
        await Expect(Page.Locator($"#{kind}Preview")).ToHaveAttributeAsync("src", $"http://exam-media.test/original.{(kind == "image" ? "png" : "mp3")}");
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
        Assert.False(await Page.EvaluateAsync<bool>("""
            () => { const event = new Event('beforeunload', {cancelable: true}); window.dispatchEvent(event); return event.defaultPrevented; }
            """));
    }

    [Fact]
    public async Task UploadInProgress_TextSaveCannotClaimUpToDate_AndNavigationWaits()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await SetupAsync(put: async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { Status = 200 });
        });
        try
        {
            await ChooseAsync("image");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Page.GetByLabel("Question text").FillAsync("Saved while uploading");
            await Page.WaitForFunctionAsync("() => window.savedCount > 0");
            await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Uploading media...");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Question 2", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Media save in progress", Exact = true })).ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Stay on this question", Exact = true }).ClickAsync();
            Assert.Equal(0, _navigations);
            release.TrySetResult();
            await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
            Assert.Contains("Saved while uploading", _saved.Last());
            Assert.Contains("exams/image/replacement", _saved.Last());
            await Page.GetByRole(AriaRole.Button, new() { Name = "Question 2", Exact = true }).ClickAsync();
            await Expect(Page.GetByLabel("Question text")).ToHaveValueAsync("Question two");
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("image")]
    [InlineData("audio")]
    public async Task RemovalFailure_RemainsMarkedAfterTextSave_AndCanRetry(string kind)
    {
        var fail = true;
        await SetupAsync(remove: route => route.FulfillAsync(new()
        {
            Status = fail ? 400 : 200,
            ContentType = "application/json",
            Body = fail ? """{"success":false,"errorMessage":"Removal failed."}""" : """{"success":true}"""
        }));
        await Page.Locator(kind == "image" ? "#removeImageBtn" : "#removeAudioBtn").ClickAsync();
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = $"Remove {kind}", Exact = true }).ClickAsync();
        await Expect(Feedback(kind)).ToContainTextAsync("removal not saved");
        await Page.GetByLabel("Question text").FillAsync("Text saved after removal failed");
        await Page.WaitForFunctionAsync("() => window.savedCount > 0");
        await Expect(Feedback(kind)).ToContainTextAsync("removal not saved");
        fail = false;
        await RetryAsync(kind);
        await Expect(Feedback(kind)).ToBeHiddenAsync();
        await Expect(Page.Locator($"input[name='{ReferenceName(kind)}']")).ToHaveValueAsync("");
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
    }

    [Fact]
    public async Task DiscardAfterUncertainReferenceSave_ExplicitlyPersistsTheOriginalReference()
    {
        var reference = "exams/image/original";
        await SetupAsync(save: async route =>
        {
            if ((route.Request.PostData ?? "").Contains("exams/image/replacement", StringComparison.Ordinal))
            {
                reference = "exams/image/replacement";
                await route.FulfillAsync(new() { Status = 400, ContentType = "application/json", Body = """{"success":false,"errorMessage":"The response was lost after committing."}""" });
            }
            else
            {
                reference = "exams/image/original";
                await SavedAsync(route);
            }
        });
        await ChooseAsync("image");
        await Expect(Feedback("image")).ToContainTextAsync("not saved");
        Assert.Equal("exams/image/replacement", reference);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Discard image upload", Exact = true }).ClickAsync();
        await Expect(Feedback("image")).ToBeHiddenAsync();
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
        Assert.Equal("exams/image/original", reference);
        Assert.Single(_saved);
        Assert.Equal(1, _uploads);
    }

    [Fact]
    public async Task ReplacedSelection_LatePreparationCannotOverwriteTheNewerUpload()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        await SetupAsync(prepare: async route =>
        {
            var older = first;
            first = false;
            if (older)
            {
                started.TrySetResult();
                await release.Task;
            }
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new { success = true, putUrl = "http://exam-media.test/upload", objectKey = older ? "exams/image/older" : "exams/image/newer" })
            });
        });
        try
        {
            await ChooseAsync("image");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Page.Locator("#imageInput").SetInputFilesAsync(new FilePayload
            {
                Name = "newer.png",
                MimeType = "image/png",
                Buffer = Encoding.UTF8.GetBytes("Newer selection")
            });
            await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
            release.TrySetResult();
            await Page.WaitForFunctionAsync("() => performance.getEntriesByType('resource').filter(item => item.name.includes('handler=CreateImageUpload')).length === 2");
            await Page.GetByLabel("Question text").FillAsync("After the stale response");
            await Page.WaitForFunctionAsync("() => window.savedCount === 2");
            Assert.Equal(1, _uploads);
            Assert.Contains("exams/image/newer", _saved.Last());
            Assert.DoesNotContain("exams/image/older", _saved.Last());
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("image")]
    [InlineData("audio")]
    public async Task UnchangedDraft_CreatesQuestionBeforeUpload_AndRetainsFailureForRetry(string kind)
    {
        var fail = true;
        await SetupAsync(put: route => route.FulfillAsync(new() { Status = fail ? 500 : 200 }), draft: true);
        await ChooseAsync(kind);
        await Expect(Feedback(kind)).ToContainTextAsync("not saved");
        await Expect(Page.Locator("input[name='QuestionId']")).ToHaveValueAsync("12");
        Assert.Single(_saved);
        fail = false;
        await RetryAsync(kind);
        await Expect(Feedback(kind)).ToBeHiddenAsync();
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
        Assert.Equal(2, _saved.Count);
    }

    [Fact]
    public async Task TextEditedDuringSave_IsSavedAgainRatherThanMarkedAsAlreadySaved()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        await SetupAsync(save: async route =>
        {
            if (first)
            {
                first = false;
                started.TrySetResult();
                await release.Task;
            }
            await SavedAsync(route);
        });
        try
        {
            await Page.GetByLabel("Question text").FillAsync("First edit");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Page.GetByLabel("Question text").FillAsync("Latest edit");
            release.TrySetResult();
            await Page.WaitForFunctionAsync("() => window.savedCount === 2");
            Assert.Contains("First edit", _saved[0]);
            Assert.Contains("Latest edit", _saved[1]);
            await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
        }
        finally { release.TrySetResult(); }
    }

    private ILocator Feedback(string kind) => Page.Locator($"[data-{kind}-ui] [data-media-feedback]");
    private Task RetryAsync(string kind) => Page.GetByRole(AriaRole.Button, new() { Name = $"Retry {kind} save", Exact = true }).ClickAsync();
    private static string ReferenceName(string kind) => kind == "image" ? "ImageReference" : "AudioReference";
    private Task ChooseAsync(string kind) => Page.Locator($"#{kind}Input").SetInputFilesAsync(new FilePayload
    {
        Name = kind == "image" ? "replacement.png" : "replacement.mp3",
        MimeType = kind == "image" ? "image/png" : "audio/mpeg",
        Buffer = Encoding.UTF8.GetBytes("Selected media")
    });

    private Task PreparedAsync(IRoute route)
    {
        var kind = (route.Request.PostData ?? "").Contains("replacement.png", StringComparison.Ordinal) ? "image" : "audio";
        return route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new { success = true, putUrl = "http://exam-media.test/upload", objectKey = $"exams/{kind}/replacement" })
        });
    }

    private async Task SavedAsync(IRoute route)
    {
        _saved.Add(route.Request.PostData ?? "");
        await route.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true,"questionId":12}""" });
        await Page.EvaluateAsync("window.savedCount = (window.savedCount || 0) + 1");
    }

    private async Task SetupAsync(Func<IRoute, Task>? put = null, Func<IRoute, Task>? prepare = null,
        Func<IRoute, Task>? save = null, Func<IRoute, Task>? remove = null, bool draft = false)
    {
        await Page.RouteAsync("http://exam-media.test/upload", route =>
        {
            _uploads++;
            return put?.Invoke(route) ?? route.FulfillAsync(new() { Status = 200 });
        });
        await Page.RouteAsync($"{EditorUrl}?handler=*", async route =>
        {
            var handler = new Uri(route.Request.Url).Query;
            if (handler.StartsWith("?handler=CreateImageUpload", StringComparison.Ordinal))
            {
                _prepared++;
                await (prepare?.Invoke(route) ?? PreparedAsync(route));
            }
            else if (handler.StartsWith("?handler=SaveQuestion", StringComparison.Ordinal))
                await (save?.Invoke(route) ?? SavedAsync(route));
            else if (handler.StartsWith("?handler=DeleteQuestionFile", StringComparison.Ordinal))
                await (remove?.Invoke(route) ?? route.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true}""" }));
            else if (handler.StartsWith("?handler=QuestionShell", StringComparison.Ordinal))
            {
                _navigations++;
                await route.FulfillAsync(new() { ContentType = "text/html", Body = """<div id="question-shell" data-question-id="13"><form data-question-edit-form><input name="QuestionId" value="13"><label>Question text<input name="Text" value="Question two"></label></form></div>""" });
            }
            else throw new InvalidOperationException($"Unexpected request: {handler}");
        });
        await Page.RouteAsync(EditorUrl, route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = $$"""
                <!doctype html><html><head><style>.exam-hidden { display:none !important; }</style></head><body>
                <input id="ExamId" value="5"><input id="UploadsEnabled" value="true">
                <span id="editorSaveStatus">Up-to-date</span>
                <button id="editorSaveRetry" type="button" disabled><i id="editorSaveIcon"></i>Retry unsaved changes</button>
                <button type="button" data-action="load-question" data-question-id="13">Question 2</button>
                <div id="questionHost"><div id="question-shell" data-question-id="{{(draft ? 0 : 12)}}" {{(draft ? "data-draft='true'" : "")}}><form data-question-edit-form>
                <input type="hidden" name="QuestionId" value="{{(draft ? 0 : 12)}}"><input type="hidden" name="__RequestVerificationToken" value="test-token">
                <label>Question text<input name="Text" value="Original question"></label><span data-save-status>Up-to-date</span>
                {{MediaMarkup("image")}}
                {{MediaMarkup("audio")}}
                </form></div></div>
                <script>window.notify = () => {}; window.savedCount = 0;</script>
                </body></html>
                """
        }));
        await Page.GotoAsync(EditorUrl);
        await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("exam-editor.js") });
        await Page.EvaluateAsync("document.dispatchEvent(new Event('DOMContentLoaded'))");
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date");
    }

    private static string MediaMarkup(string kind) => $$"""
        <div data-{{kind}}-ui>
            <input type="hidden" name="{{ReferenceName(kind)}}" value="exams/{{kind}}/original">
            <div id="{{kind}}UploadBox" class="exam-hidden">Upload {{kind}}</div>
            {{(kind == "image" ? "<img id='imagePreview' src='http://exam-media.test/original.png' alt='Image preview'>" : "<audio id='audioPreview' src='http://exam-media.test/original.mp3'></audio>")}}
            <button type="button" id="change{{(kind == "image" ? "Image" : "Audio")}}Btn">Change {{kind}}</button>
            <button type="button" id="remove{{(kind == "image" ? "Image" : "Audio")}}Btn">Remove {{kind}}</button>
            <input type="file" id="{{kind}}Input" name="{{kind}}File" hidden>
            <div class="exam-hidden" data-media-feedback role="status">
                <i data-media-warning aria-hidden="true"></i><span data-media-message></span>
                <button type="button" data-media-retry aria-label="Retry {{kind}} save">Retry</button>
                <button type="button" data-media-discard aria-label="Discard {{kind}} upload">Discard upload</button>
            </div>
        </div>
        """;
}
