using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ExamSubmissionInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    private const string ExamUrl = "http://exam-submission.test/en-us/examination/level-test/8a669b08-8c79-4d7d-aea5-7557e3d17fb7";
    private const string CompletedUrl = "http://exam-submission.test/en-us/examination/completed/8a669b08-8c79-4d7d-aea5-7557e3d17fb7";

    [Fact]
    public async Task SubmitExam_WhenAnswerSaveFails_KeepsAttemptOpenAndAllowsRetry()
    {
        var failSave = true;
        var handlers = new List<string>();
        await SetupExamAsync(async route =>
        {
            var handler = new Uri(route.Request.Url).Query;
            handlers.Add(handler);
            if (handler == "?handler=SubmitAnswer" && failSave)
            {
                await route.FulfillAsync(new()
                {
                    Status = 500,
                    ContentType = "application/json",
                    Body = """{"success":false,"message":"Saving temporarily unavailable."}"""
                });
                return;
            }
            await RespondSuccessfullyAsync(route, handler);
        });

        await Page.GetByLabel("Answer").FillAsync("Amsterdam");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit exam" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("could not be saved");
        await Expect(Page).ToHaveURLAsync(ExamUrl);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Submit exam" })).ToBeEnabledAsync();
        Assert.DoesNotContain("?handler=SubmitExam", handlers);
        Assert.DoesNotContain("?handler=ValidateMissingQuestions", handlers);

        failSave = false;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit exam" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(CompletedUrl);
        Assert.Equal(1, handlers.Count(handler => handler == "?handler=SubmitExam"));
    }

    [Fact]
    public async Task SubmitExam_WhenAnswerChangesDuringSave_PersistsLatestAnswerBeforeCompleting()
    {
        var releaseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var savedAnswers = new List<string>();
        await SetupExamAsync(async route =>
        {
            var handler = new Uri(route.Request.Url).Query;
            if (handler == "?handler=SubmitAnswer")
            {
                savedAnswers.Add(route.Request.PostData ?? string.Empty);
                if (savedAnswers.Count == 1)
                {
                    firstSaveStarted.TrySetResult();
                    await releaseFirstSave.Task;
                }
            }
            await RespondSuccessfullyAsync(route, handler);
        });

        await Page.GetByLabel("Answer").FillAsync("Rotterdam");
        await Page.EvaluateAsync("() => { window.submitCurrentAnswer(); }");
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Page.GetByLabel("Answer").FillAsync("Amsterdam");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit exam" }).ClickAsync();
        releaseFirstSave.TrySetResult();

        await Expect(Page).ToHaveURLAsync(CompletedUrl);
        Assert.Equal(2, savedAnswers.Count);
        Assert.Contains("Rotterdam", savedAnswers[0], StringComparison.Ordinal);
        Assert.Contains("Amsterdam", savedAnswers[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitExam_WhenTimeExpires_StillCompletesUsingPreviouslySavedAnswers()
    {
        var submitted = false;
        await SetupExamAsync(async route =>
        {
            var handler = new Uri(route.Request.Url).Query;
            if (handler == "?handler=SubmitAnswer")
            {
                await route.FulfillAsync(new()
                {
                    Status = 409,
                    ContentType = "application/json",
                    Body = """{"success":false,"message":"Time limit reached."}"""
                });
                return;
            }
            if (handler == "?handler=SubmitExam")
                submitted = true;
            await RespondSuccessfullyAsync(route, handler);
        });

        await Page.GetByLabel("Answer").FillAsync("Late answer");
        await Page.EvaluateAsync("() => { window.submitExam({ force: true, timeoutTriggered: true }); }");

        await Expect(Page).ToHaveURLAsync(CompletedUrl);
        Assert.True(submitted);
    }

    private async Task SetupExamAsync(Func<IRoute, Task> handleSubmission)
    {
        await Page.RouteAsync($"{ExamUrl}?handler=*", handleSubmission);
        await Page.RouteAsync(CompletedUrl, route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = "<h1>Examination completed</h1>"
        }));
        await Page.RouteAsync(ExamUrl, route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = """
                <!doctype html>
                <html><body>
                    <input id="RequestVerificationToken" type="hidden" value="token" />
                    <input id="ExamId" type="hidden" value="7" />
                    <input id="RecordId" type="hidden" value="8a669b08-8c79-4d7d-aea5-7557e3d17fb7" />
                    <input id="questionId" type="hidden" value="11" />
                    <label for="answer">Answer</label>
                    <textarea id="answer" name="answer"></textarea>
                    <button id="submitExam" type="button">Submit exam</button>
                    <p role="alert"></p>
                    <script>
                        window.notify = message => document.querySelector('[role="alert"]').textContent = message;
                    </script>
                </body></html>
                """
        }));
        await Page.GotoAsync(ExamUrl);
        await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("exam-pagination.js") });
        await Page.EvaluateAsync("document.dispatchEvent(new Event('DOMContentLoaded'))");
    }

    private static Task RespondSuccessfullyAsync(IRoute route, string handler)
        => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = handler == "?handler=SubmitExam"
                ? $$"""{"success":true,"redirectUrl":"{{CompletedUrl}}"}"""
                : """{"success":true}"""
        });
}
