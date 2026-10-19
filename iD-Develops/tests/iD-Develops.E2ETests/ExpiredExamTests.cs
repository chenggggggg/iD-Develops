using System.Text.RegularExpressions;
using iD_Develops.Models;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ExpiredExamTests : Microsoft.Playwright.Xunit.PageTest
{
    [Theory]
    [InlineData("en-us")]
    [InlineData("nl-nl")]
    public async Task ExpiredAttempt_ConcurrentVisits_ShowCompletionAndProtectedResults(string culture)
    {
        await using var data = await ExamAttemptFixture.CreateAsync();
        var visits = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            var visitor = await Context.NewPageAsync();
            try
            {
                var response = await visitor.GotoAsync(data.AttemptUrl(culture), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                Assert.NotNull(response);
                return (response.Status, visitor.Url);
            }
            finally
            {
                await visitor.CloseAsync();
            }
        }));
        var state = await data.ReadRecordAsync();
        Assert.All(visits, visit =>
        {
            Assert.True(visit.Status == 200,
                $"Completion returned {visit.Status}; attempt status={state.ExamStatus}, score={state.Score}, version={state.ExamVersionId}.");
            Assert.Equal(data.CompletionUrl(culture), visit.Url);
        });

        await Page.GotoAsync(data.CompletionUrl(culture), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByRole(AriaRole.Heading, new()
        {
            Name = culture == "nl-nl" ? "De Nederlandse taal wacht op je!" : "Dutch Language Awaits!",
            Exact = true
        })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new()
        {
            Name = culture == "nl-nl" ? "Stuur mijn resultaat!" : "Send my results!",
            Exact = true
        })).ToBeVisibleAsync();

        await Page.GotoAsync(data.ResultsUrl(culture), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Exam.Name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("100%", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Amsterdam", new() { Exact = true }).First).ToBeVisibleAsync();

        var record = await data.ReadRecordAsync();
        Assert.Equal(ExamStatus.Completed, record.ExamStatus);
        Assert.Equal(2d, record.Score);
        Assert.Single(await data.ReadTransitionsAsync());

        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/examination/results/{record.Id}?token=invalid",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Exam.Name, Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Amsterdam", new() { Exact = true })).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("en-us", "Asia/Bangkok", true)]
    [InlineData("nl-nl", "Europe/Amsterdam", true)]
    [InlineData("en-us", "Asia/Bangkok", false)]
    [InlineData("nl-nl", "Europe/Amsterdam", false)]
    public async Task Countdown_AutomaticallyCompletesWithUnansweredQuestions(string culture, string timeZone, bool answerQuestion)
    {
        await using var data = await ExamAttemptFixture.CreateAsync(expired: false, savedAnswer: false, includeUnansweredQuestion: true);
        await using var context = await Browser.NewContextAsync(new() { TimezoneId = timeZone });
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync(data.AttemptUrl(culture), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(page.Locator("#countdown")).ToHaveAttributeAsync("data-duration", new Regex(@"^\d{4}-\d{2}-\d{2}T.*Z$"));
        await Expect(page.Locator("#countdown")).ToHaveTextAsync(new Regex(@"^00:01:\d{2}$"));
        if (answerQuestion)
        {
            var saved = await page.RunAndWaitForResponseAsync(
                () => page.GetByRole(AriaRole.Textbox).FillAsync("Amsterdam"),
                response => response.Request.Method == "POST" && response.Url.Contains("handler=SubmitAnswer", StringComparison.Ordinal));
            Assert.Equal(200, saved.Status);
        }

        // Move the persisted deadline as well as the browser clock; server time remains authoritative.
        await data.ExpireAsync();
        await page.Clock.FastForwardAsync(120_000);

        await Expect(page).ToHaveURLAsync(data.CompletionUrl(culture));
        await Expect(page.GetByRole(AriaRole.Button, new()
        {
            Name = culture == "nl-nl" ? "Stuur mijn resultaat!" : "Send my results!",
            Exact = true
        })).ToBeVisibleAsync();
        var record = await data.ReadRecordAsync();
        Assert.Equal(ExamStatus.Completed, record.ExamStatus);
        Assert.Equal(RecordStatusReason.DeadlinePassed, record.StatusReason);
        Assert.Equal(answerQuestion ? 2d : 0d, record.Score);
        Assert.Single(await data.ReadTransitionsAsync());
    }
}
