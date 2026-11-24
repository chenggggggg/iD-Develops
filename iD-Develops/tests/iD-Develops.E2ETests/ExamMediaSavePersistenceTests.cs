using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ExamMediaSavePersistenceTests : Microsoft.Playwright.Xunit.PageTest
{
    [Theory]
    [InlineData("image")]
    [InlineData("audio")]
    public async Task LiveEditor_FailedUploadWarningSurvivesSuccessfulQuestionSave(string kind)
    {
        await using var administrator = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var data = await ExamAttemptFixture.CreateAsync(expired: false, savedAnswer: false);
        var questionId = data.Exam.Questions.Single().Id;
        var path = $"/exams/{data.Exam.Id}/edit?questionId={questionId}";
        await Page.RouteAsync("**/*?handler=CreateImageUpload", route => route.FulfillAsync(new()
        {
            Status = 400,
            ContentType = "application/json",
            Body = """{"success":false,"errorMessage":"Upload storage temporarily unavailable."}"""
        }));
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await Page.GetByLabel("E-mail").FillAsync(administrator.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(administrator.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
        await Expect(Page.Locator("input[name='QuestionId']")).ToHaveValueAsync(questionId.ToString());
        await Expect(Page.Locator("[data-open-answer-editor]")).ToHaveAttributeAsync("data-open-editor-ready", "true");
        await Expect(Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Maximum points", Exact = true }))
            .ToHaveAttributeAsync("readonly", "");

        // Enable only this page's picker; the upload endpoint is intercepted above.
        // The running Local environment still has object storage disabled.
        await Page.EvaluateAsync("document.getElementById('UploadsEnabled').value = 'true'");
        await Page.Locator($"#{kind}Input").SetInputFilesAsync(new FilePayload
        {
            Name = kind == "image" ? "example.png" : "example.mp3",
            MimeType = kind == "image" ? "image/png" : "audio/mpeg",
            Buffer = Encoding.UTF8.GetBytes("Failed upload regression")
        });
        var feedback = Page.Locator($"[data-{kind}-ui] [data-media-feedback]");
        await Expect(feedback).ToBeVisibleAsync();
        await Expect(feedback).ToContainTextAsync("not saved");
        var updatedLabel = $"Answer saved after failed {kind} upload";
        var saved = await Page.RunAndWaitForResponseAsync(
            () => Page.Locator("[data-open-label]").FillAsync(updatedLabel),
            response => response.Url.Contains("handler=SaveQuestion", StringComparison.Ordinal) && response.Request.Method == "POST");
        Assert.True(saved.Ok);
        Assert.True((await saved.JsonAsync())!.Value.GetProperty("success").GetBoolean());
        await using var db = LocalTestApplication.CreateDbContext();
        var savedQuestion = await db.OpenQuestions.AsNoTracking().SingleAsync(question => question.Id == questionId);
        Assert.Equal(updatedLabel, savedQuestion.OpenAnswerFields.Single().Label);
        Assert.Equal(2, savedQuestion.Score);
        await Expect(feedback).ToContainTextAsync("not saved");
        await Expect(Page.Locator("#editorSaveStatus")).ToHaveTextAsync("Media not saved");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = $"Retry {kind} save", Exact = true })).ToBeVisibleAsync();
    }
}
