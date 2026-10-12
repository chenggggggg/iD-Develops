using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ExamEditorInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task HeaderInlineEditor_SkipsUnchangedValuesAndSavesFormattedTime()
    {
        var saveRequests = new List<string>();
        await Page.RouteAsync("http://exam-editor.test/editor?handler=SaveHeaderSettings", async route =>
        {
            saveRequests.Add(route.Request.PostData ?? string.Empty);
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = """{"success":true,"timeLimitSeconds":5445,"timeLimitDisplay":"01:30:45"}"""
            });
        });
        await Page.RouteAsync("http://exam-editor.test/editor", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = """
                <!doctype html>
                <html>
                <body>
                    <input name="__RequestVerificationToken" value="token" />
                    <div class="exam-header-label exam-header-name-edit" data-exam-header-edit="name" data-exam-id="7" data-exam-header-saved-value="Placement test">
                        <input class="exam-header-name-input" data-exam-header-input value="Placement test" size="14" readonly />
                        <i class="fa-solid fa-pencil exam-header-edit-icon" aria-hidden="true"></i>
                    </div>
                    <div data-exam-header-edit="timeLimit" data-exam-id="7" data-exam-header-saved-value="00:30:00">
                        <span data-exam-header-display>00:30:00</span>
                        <span data-exam-time-editor>
                            <input type="number" min="0" max="99999" data-exam-time-part="hours" value="00" />
                            <input type="number" min="0" max="59" data-exam-time-part="minutes" value="30" />
                            <input type="number" min="0" max="59" data-exam-time-part="seconds" value="00" />
                        </span>
                    </div>
                    <script>window.notices = []; window.notify = (message) => window.notices.push(message);</script>
                </body>
                </html>
                """
        }));

        await Page.GotoAsync("http://exam-editor.test/editor");
        await Page.AddStyleTagAsync(new() { Path = TestAssetPaths.Css("portal.css") });
        await Page.AddStyleTagAsync(new() { Path = TestAssetPaths.Css("examination.css") });
        await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("exam-header-inline-edit.js") });

        var nameField = Page.Locator("[data-exam-header-edit='name']");
        var nameInput = nameField.Locator("input");
        await Expect(nameInput).ToHaveAttributeAsync("readonly", "");
        var restingGeometry = await nameField.EvaluateAsync<float[]>(
            """
            element => {
                const input = element.querySelector('[data-exam-header-input]');
                const fieldRect = element.getBoundingClientRect();
                const inputRect = input.getBoundingClientRect();
                return [
                    fieldRect.top,
                    fieldRect.height,
                    inputRect.top,
                    inputRect.height,
                    Number.parseFloat(getComputedStyle(element).borderTopWidth)
                ];
            }
            """);
        Assert.InRange(Math.Abs(restingGeometry[2] - restingGeometry[0]), 0, 0.1);
        Assert.InRange(Math.Abs(restingGeometry[3] - restingGeometry[1]), 0, 0.1);
        Assert.Equal(0, restingGeometry[4]);
        var restingNameHeight = await nameField.EvaluateAsync<float>("element => element.getBoundingClientRect().height");
        await nameField.HoverAsync();
        Assert.NotEqual("none", await nameInput.EvaluateAsync<string>("element => getComputedStyle(element).boxShadow"));
        await nameField.ClickAsync();
        await Expect(nameInput).ToBeEditableAsync();
        Assert.Equal("none", await nameInput.EvaluateAsync<string>("element => getComputedStyle(element).boxShadow"));
        var editingNameHeight = await nameField.EvaluateAsync<float>("element => element.getBoundingClientRect().height");
        Assert.InRange(Math.Abs(editingNameHeight - restingNameHeight), 0, 0.1);
        await nameInput.PressAsync("Enter");
        await Page.WaitForTimeoutAsync(150);

        Assert.Empty(saveRequests);
        Assert.Equal(0, await Page.EvaluateAsync<int>("window.notices.length"));
        await Expect(nameInput).ToHaveAttributeAsync("readonly", "");

        await nameField.ClickAsync();
        await nameInput.FillAsync("Updated placement test");
        await nameInput.PressAsync("Enter");

        await Expect(nameInput).ToHaveAttributeAsync("readonly", "");
        await Expect(nameInput).ToHaveValueAsync("Updated placement test");
        Assert.Single(saveRequests);
        Assert.Contains("field=name", saveRequests[0], StringComparison.Ordinal);
        Assert.Contains("name=Updated+placement+test", saveRequests[0], StringComparison.Ordinal);

        saveRequests.Clear();
        await Page.EvaluateAsync("window.notices = []");

        var timeField = Page.Locator("[data-exam-header-edit='timeLimit']");
        var hours = timeField.Locator("[data-exam-time-part='hours']");
        var minutes = timeField.Locator("[data-exam-time-part='minutes']");
        var seconds = timeField.Locator("[data-exam-time-part='seconds']");

        await timeField.ClickAsync();
        await Expect(hours).ToBeFocusedAsync();
        await hours.FillAsync("1");
        await hours.PressAsync("Tab");
        await Expect(minutes).ToBeFocusedAsync();
        await minutes.FillAsync("30");
        await minutes.PressAsync("Tab");
        await Expect(seconds).ToBeFocusedAsync();
        await seconds.FillAsync("45");
        await seconds.PressAsync("Enter");

        await Expect(Page.Locator("[data-exam-header-edit='timeLimit'] [data-exam-header-display]"))
            .ToHaveTextAsync("01:30:45");
        Assert.Single(saveRequests);
        Assert.Contains("timeLimitSeconds=5445", saveRequests[0], StringComparison.Ordinal);
        Assert.Equal(1, await Page.EvaluateAsync<int>("window.notices.length"));
    }

    [Fact]
    public async Task RichTextEditor_OpenAndCloseWithoutChanges_DoesNotQueueSave()
    {
        await Page.SetContentAsync(
            """
            <!doctype html>
            <html>
            <body>
                <div data-exam-rich-field>
                    <button type="button" data-exam-rich-preview>Context text</button>
                    <textarea id="contextEditor" data-exam-rich-textarea>Context text</textarea>
                </div>
                <button id="outside" type="button">Outside</button>
            </body>
            </html>
            """);

        await Page.EvaluateAsync(
            """
            window.richChangeCount = 0;
            window.editorRegistry = new Map();
            window.tinymce = {
                get: id => window.editorRegistry.get(id) || null,
                remove: editor => window.editorRegistry.delete(editor.id),
                triggerSave: () => {}
            };
            window.TinyMceHelpers = {
                isAvailable: () => true,
                initEditors: (selectors, options) => {
                    const textarea = document.querySelector(selectors[0]);
                    const handlers = {};
                    const editor = {
                        id: textarea.id,
                        content: textarea.value,
                        on: (names, handler) => names.split(' ').forEach(name => handlers[name] = handler),
                        save: () => { textarea.value = editor.content; },
                        focus: () => {},
                        setDirty: () => {},
                        handlers
                    };
                    window.fakeRichEditor = editor;
                    window.editorRegistry.set(editor.id, editor);
                    options.setup(editor);
                    handlers.setcontent?.();
                    handlers.init?.();
                }
            };
            """);

        await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("exam-rich-edit-fields.js") });
        await Page.EvaluateAsync(
            """
            window.ExamRichEditFields.init(document, {
                onChange: () => window.richChangeCount++,
                onClose: () => window.richChangeCount++
            });
            """);

        await Page.Locator("[data-exam-rich-preview]").ClickAsync();
        Assert.Equal(0, await Page.EvaluateAsync<int>("window.richChangeCount"));

        await Page.EvaluateAsync("window.fakeRichEditor.handlers.blur()");
        await Page.WaitForTimeoutAsync(180);
        Assert.Equal(0, await Page.EvaluateAsync<int>("window.richChangeCount"));
    }

    [Fact]
    public async Task AutosaveStatus_ReturnsToUpToDateWhenPayloadDidNotChange()
    {
        await Page.SetContentAsync(
            """
            <!doctype html>
            <html>
            <body>
                <input id="ExamId" value="5" />
                <span id="editorSaveStatus">Up-to-date</span>
                <button id="editorSaveRetry" type="button" disabled><i id="editorSaveIcon" class="fa-solid fa-check"></i></button>
                <div id="questionHost">
                    <div id="question-shell" data-question-id="12">
                        <form data-question-edit-form>
                            <input name="QuestionId" value="12" />
                            <input name="Text" value="Unchanged question" />
                        </form>
                    </div>
                </div>
            </body>
            </html>
            """);

        await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("exam-editor.js") });
        await Page.EvaluateAsync("document.dispatchEvent(new Event('DOMContentLoaded'))");
        await Page.WaitForTimeoutAsync(100);

        await Page.EvaluateAsync(
            "document.querySelector('input[name=Text]').dispatchEvent(new Event('input', { bubbles: true }))");

        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Unsaved changes...");
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Up-to-date", new() { Timeout = 2_000 });
    }

    [Fact]
    public async Task ExamActions_UseTheCustomConfirmationContent()
    {
        await Page.SetContentAsync(
            """
            <!doctype html>
            <html>
            <body>
                <button type="button" data-exam-confirm-action="archive" data-exam-name="Placement test">Archive</button>
                <div id="examActionConfirmDialog">
                    <h2 data-exam-confirm-title></h2>
                    <p data-exam-confirm-message></p>
                    <button type="button" data-exam-confirm-submit><i class="fa-solid fa-triangle-exclamation"></i><span data-exam-confirm-submit-label></span></button>
                    <button type="button" data-portal-dialog-close="examActionConfirmDialog">Cancel</button>
                </div>
            </body>
            </html>
            """);

        await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("exams-list.js") });
        await Page.EvaluateAsync("initializeExamActionConfirmation()");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();

        await Expect(Page.Locator("[data-exam-confirm-title]")).ToHaveTextAsync("Archive exam");
        await Expect(Page.Locator("[data-exam-confirm-message]")).ToContainTextAsync("Placement test");
        await Expect(Page.Locator("[data-exam-confirm-submit-label]")).ToHaveTextAsync("Archive exam");
    }
}
