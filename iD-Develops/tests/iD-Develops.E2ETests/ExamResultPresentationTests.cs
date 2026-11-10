using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Pages.Examination;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ExamResultPresentationTests : DiagnosticPageTest
{
    [Theory]
    [InlineData("en-us", false)]
    [InlineData("en-us", true)]
    [InlineData("nl-nl", false)]
    [InlineData("nl-nl", true)]
    public async Task HistoricalResultAndCompletionRemainReadableAndUseTheActualExamTaken(string culture, bool mobile)
    {
        await using var data = await ResultFixture.CreateAsync();
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
        var response = await Page.GotoAsync(data.ResultsUrl(culture), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(200, response!.Status);
        AssertPrivateResponse(response);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.OriginalName, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-result-version]")).ToContainTextAsync(culture == "nl-nl" ? "Versie 1" : "Version 1");
        await Expect(Page.Locator("[data-result-version]")).ToContainTextAsync("00:02:00");
        await Expect(Page.Locator("[data-result-percentage]")).ToHaveTextAsync("33%");
        await Expect(Page.Locator("[data-result-unanswered]")).ToHaveTextAsync("1");
        await Expect(Page.Locator("[data-result-grade]")).ToHaveTextAsync(culture == "nl-nl" ? "Klaar voor de volgende stap" : "Ready for the next step");
        await Expect(Page.Locator("[data-result-feedback]")).ToHaveTextAsync(culture == "nl-nl" ? "Oorspronkelijke feedback" : "Original feedback");
        await Expect(Page.Locator("[data-question-number='1'] dd")).ToHaveTextAsync(new[] { "Amsterdam", "Amsterdam" });
        await Expect(Page.GetByText("Original scenario", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Original question feedback", new() { Exact = true })).ToBeVisibleAsync();
        var audio = Page.Locator("[data-question-number='1'] audio");
        // The consent manager can defer media loading; either state must retain the historical source.
        var audioSource = await audio.EvaluateAsync<string?>("element => element.getAttribute('src') || element.getAttribute('data-cookieblock-src')");
        Assert.EndsWith("/original-audio.wav", audioSource);

        var choice = Page.Locator("[data-question-number='2']");
        await choice.Locator("summary").PressAsync("Enter");
        await Expect(choice).ToHaveAttributeAsync("open", "");
        await Expect(choice.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Train" })).ToBeVisibleAsync();
        await Expect(choice.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Bus" })).ToBeVisibleAsync();
        await Expect(choice.GetByText(culture == "nl-nl" ? "Jouw antwoord" : "Your answer", new() { Exact = true })).ToBeVisibleAsync();
        var blank = Page.Locator("[data-question-number='3']");
        await blank.Locator("summary").PressAsync("Enter");
        await Expect(blank).ToHaveAttributeAsync("open", "");
        await Expect(blank).ToContainTextAsync(culture == "nl-nl" ? "Geen antwoord gegeven" : "No answer submitted");
        await Expect(Page.GetByText("Updated current exam", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Changed question", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Future option", new() { Exact = false })).ToHaveCountAsync(0);
        Assert.True(await Page.Locator("[data-exam-result]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await Page.EvaluateAsync("window.scrollTo(0, 0)");
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"b10-results-{culture}-{mobile}.png"), FullPage = true });

        var completionResponse = await Page.GotoAsync(data.CompletionUrl(culture), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        AssertPrivateResponse(completionResponse);
        var image = Page.Locator("[data-completion-image]");
        await Expect(image).ToHaveJSPropertyAsync("naturalWidth", 1920);
        await Expect(image).ToHaveJSPropertyAsync("naturalHeight", 1080);
        Assert.Contains("exam-completed-wallpaper.webp", await image.GetAttributeAsync("src"));
        if (mobile) await Expect(Page.Locator("[data-completion-image-panel]")).ToBeHiddenAsync();
        else await Expect(Page.Locator("[data-completion-image-panel]")).ToBeVisibleAsync();
        await Expect(Page.GetByLabel(culture == "nl-nl" ? "Naam:" : "Name:", new() { Exact = true })).ToBeEditableAsync();
        await Expect(Page.GetByLabel(culture == "nl-nl" ? "E-mail:" : "Email:", new() { Exact = true })).ToBeEditableAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = culture == "nl-nl" ? "Stuur mijn resultaat!" : "Send my results!", Exact = true })).ToBeVisibleAsync();
        Assert.True(await Page.Locator("[data-exam-completion]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"b10-completion-{culture}-{mobile}.png"), FullPage = true });
        var record = await data.Db.Records.AsNoTracking().SingleAsync(item => item.Id == data.Record.Id);
        Assert.Equal(ExamStatus.Completed, record.ExamStatus);
        Assert.Equal(2, record.Score);
        Assert.Equal(data.Record.ExamVersionId, record.ExamVersionId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("mismatched-record")]
    [InlineData("unfinished")]
    public async Task UnavailableResultDoesNotExposeAttemptContent(string state)
    {
        await using var data = await ResultFixture.CreateAsync();
        if (state == "unfinished")
        {
            data.Record.ExamStatus = ExamStatus.InProgress;
            await data.Db.SaveChangesAsync();
        }
        var url = state switch
        {
            "missing" => $"{LocalTestApplication.PublicBaseUrl}/en-us/examination/results/{data.Record.Id}",
            "invalid" => $"{LocalTestApplication.PublicBaseUrl}/en-us/examination/results/{data.Record.Id}?token=invalid",
            "mismatched-record" => data.ResultsUrl("en-us", Guid.NewGuid()),
            _ => data.ResultsUrl("en-us")
        };
        var response = await Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        AssertPrivateResponse(response);
        await Expect(Page.Locator("[data-result-unavailable]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-result-exam-name], [data-result-grade], [data-question-number]")).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Amsterdam", new() { Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task EmptyExamHasAnExplicitEmptyReviewAndZeroScore()
    {
        await using var data = await ResultFixture.CreateAsync(empty: true);
        await Page.GotoAsync(data.ResultsUrl("en-us"), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.Locator("[data-result-percentage]")).ToHaveTextAsync("0%");
        await Expect(Page.Locator("[data-result-grade]")).ToHaveTextAsync("0 / 0");
        await Expect(Page.GetByText("There are no questions to review for this exam.", new() { Exact = true })).ToBeVisibleAsync();
    }

    private static void AssertPrivateResponse(IResponse? response)
    {
        Assert.NotNull(response);
        Assert.Contains("no-store", response.Headers["cache-control"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-cache", response.Headers["cache-control"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noindex", response.Headers["x-robots-tag"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-referrer", response.Headers["referrer-policy"]);
    }

    private sealed class ResultFixture(ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db => db;
        public Exam Exam { get; private set; } = null!;
        public iD_Develops.Models.Record Record { get; private set; } = null!;
        public string OriginalName { get; private set; } = string.Empty;

        public static async Task<ResultFixture> CreateAsync(bool empty = false)
        {
            var data = new ResultFixture(LocalTestApplication.CreateDbContext());
            try { await data.SeedAsync(empty); return data; }
            catch { await data.DisposeAsync(); throw; }
        }

        private async Task SeedAsync(bool empty)
        {
            OriginalName = $"Original level test {Guid.NewGuid():N}";
            var open = new OpenQuestion
            {
                QuestionNumber = 1,
                Text = "Where do you live?",
                Score = 2,
                Scenario = "<p>Original scenario</p>",
                MessageBeforeQuestion = "<p>Original help</p>",
                Feedback = "<p>Original question feedback</p>",
                CorrectAnswers = [new() { Text = "Amsterdam" }],
                ImageReference = $"{LocalTestApplication.PublicBaseUrl}/images/exam-completed-wallpaper.webp",
                AudioReference = $"{LocalTestApplication.PublicBaseUrl}/original-audio.wav"
            };
            var choice = new MultipleChoiceQuestion
            {
                QuestionNumber = 2,
                Text = "Choose the train.",
                Score = 2,
                MultipleChoiceAnswer = new() { AnswerA = "Train", AnswerB = "Bus" },
                CorrectAnswers = [new() { Text = "A" }]
            };
            Exam = new Exam
            {
                Name = OriginalName,
                CreatedByUserId = "e2e-result-presentation",
                TimeLimitSeconds = 120,
                IntroductionPrimaryLanguage = "Answer the questions.",
                PublishStatus = ExamPublishStatus.Published,
                Questions = empty ? [] : [open, choice, new OpenQuestion { QuestionNumber = 3, Text = "A question left blank", Score = 2, CorrectAnswers = [new() { Text = "Utrecht" }] }]
            };
            db.Exams.Add(Exam);
            await db.SaveChangesAsync();
            // The snapshot is public; the catalog exam never replaces the configured local level test.
            var version = new ExamVersion
            {
                Exam = Exam,
                VersionNumber = 1,
                Name = OriginalName,
                TimeLimitSeconds = 120,
                PublicSlug = "level-test",
                PublishedAtUtc = DateTime.UtcNow.AddDays(-1),
                IntroductionPrimaryLanguage = Exam.IntroductionPrimaryLanguage,
                CompletionTextPrimary = "<p>Oorspronkelijke feedback</p>",
                CompletionTextSecondary = "<p>Original feedback</p>",
                ResultGradeDisplayMode = empty ? ResultGradeDisplayMode.Score : ResultGradeDisplayMode.GradeBands,
                GradeBands = [new() { MinimumScore = 0, LabelPrimary = "Meer oefenen", LabelSecondary = "Keep practising" }, new() { MinimumScore = 2, LabelPrimary = "Klaar voor de volgende stap", LabelSecondary = "Ready for the next step" }],
                Questions = Exam.Questions.Select(question => new ExamVersionQuestion
                {
                    SourceQuestionId = question.Id,
                    QuestionNumber = question.QuestionNumber,
                    QuestionType = question is MultipleChoiceQuestion ? "MultipleChoice" : "Open",
                    Text = question.Text,
                    Score = question.Score,
                    Scenario = question.Scenario,
                    MessageBeforeQuestion = question.MessageBeforeQuestion,
                    Feedback = question.Feedback,
                    ImageReference = question.ImageReference,
                    AudioReference = question.AudioReference,
                    AnswerA = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer.AnswerA,
                    AnswerB = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer.AnswerB,
                    CorrectAnswers = question.CorrectAnswers.Select(answer => new ExamVersionCorrectAnswer { Text = answer.Text }).ToList()
                }).ToList()
            };
            Record = new()
            {
                Exam = Exam,
                ExamVersion = version,
                ExamStatus = ExamStatus.Completed,
                StartDateTime = DateTime.UtcNow.AddMinutes(-5),
                EndDateTime = DateTime.UtcNow.AddMinutes(-3),
                Score = empty ? 0 : 2
            };
            db.Records.Add(Record);
            if (!empty)
            {
                db.ParticipantAnswers.AddRange(
                    new ParticipantAnswer { Record = Record, Question = open, AnswerText = "Amsterdam", Timestamp = Record.StartDateTime },
                    new ParticipantAnswer { Record = Record, Question = choice, AnswerText = "B", Timestamp = Record.StartDateTime });
                open.Text = "Changed question";
                open.CorrectAnswers.Single().Text = "Rotterdam";
                choice.MultipleChoiceAnswer.AnswerB = "Future option";
                choice.CorrectAnswers.Single().Text = "B";
            }
            Exam.Name = "Updated current exam";
            Exam.TimeLimitSeconds = 15;
            Exam.PublishStatus = ExamPublishStatus.Archived;
            await db.SaveChangesAsync();
        }

        public string CompletionUrl(string culture)
            => $"{LocalTestApplication.PublicBaseUrl}/{culture}/examination/completed/{Record.Id}";

        public string ResultsUrl(string culture, Guid? requestedRecordId = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(LocalTestApplication.DatabaseConnectionString));
            services.AddDataProtection().SetApplicationName("iD-Develops").PersistKeysToDbContext<ApplicationDbContext>();
            using var provider = services.BuildServiceProvider();
            var token = ResultsModel.CreatePublicResultToken(provider.GetRequiredService<IDataProtectionProvider>(), Record.Id, "result-learner@example.test");
            return $"{LocalTestApplication.PublicBaseUrl}/{culture}/examination/results/{requestedRecordId ?? Record.Id}?token={Uri.EscapeDataString(token)}";
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Record != null)
                {
                    await db.ParticipantAnswers.Where(answer => answer.RecordId == Record.Id).ExecuteDeleteAsync();
                    await db.Records.Where(record => record.Id == Record.Id).ExecuteDeleteAsync();
                }
                if (Exam?.Id > 0)
                {
                    await db.Questions.Where(question => question.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.Exams.Where(exam => exam.Id == Exam.Id).ExecuteDeleteAsync();
                }
            }
            finally { await db.DisposeAsync(); }
        }
    }
}
