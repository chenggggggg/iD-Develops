using System.Text.Json;
using System.Text.RegularExpressions;
using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ExamTakingLayoutTests : DiagnosticPageTest
{
    [Theory]
    [InlineData(false, "en-us", 1440)]
    [InlineData(false, "nl-nl", 768)]
    [InlineData(false, "nl-nl", 390)]
    [InlineData(false, "en-us", 320)]
    [InlineData(true, "en-us", 1440)]
    [InlineData(true, "en-us", 390)]
    public async Task TakingLayoutKeepsQuestionsWithAnswersAndPreservesKeyboardAutosave(bool portal, string culture, int width)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await using var data = await TakingFixture.CreateAsync(account.User.Id, portal);
        await Page.SetViewportSizeAsync(width, 900);
        await Page.RouteAsync("**/e2e-exam-context.svg", route => route.FulfillAsync(new()
        {
            ContentType = "image/svg+xml",
            Body = """<svg xmlns="http://www.w3.org/2000/svg" width="400" height="480" viewBox="0 0 400 480"><rect width="400" height="480" fill="#eef6fb"/><rect x="32" y="32" width="336" height="416" rx="12" fill="#005190"/><text x="200" y="245" fill="white" text-anchor="middle" font-family="sans-serif" font-size="24">Question context</text></svg>"""
        }));
        await Page.RouteAsync("**/e2e-exam-audio.wav", route => route.FulfillAsync(new()
        {
            ContentType = "audio/wav",
            BodyBytes = Convert.FromBase64String("UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=")
        }));
        var path = portal ? $"/examination/{data.Record.Id}" : $"/{culture}/examination/level-test/{data.Record.Id}";
        if (portal)
            await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password, path, Page.Locator("#question-form"));
        else
            Assert.True((await Page.GotoAsync(LocalTestApplication.PublicBaseUrl + path, new() { WaitUntil = WaitUntilState.DOMContentLoaded }))!.Ok);

        await Expect(Page.Locator("#countdown")).ToHaveTextAsync(new Regex(@"^00:\d{2}:\d{2}$"));
        Assert.NotEqual("00:00:00", await Page.Locator("#countdown").InnerTextAsync());
        await Page.WaitForFunctionAsync("document.querySelector('[data-exam-question-image]')?.naturalWidth > 0");
        var metrics = await Page.EvaluateAsync<JsonElement>("""
            () => {
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const header = rect('[data-exam-header]');
                const context = rect('[data-exam-context]');
                const media = rect('[data-exam-context-media]');
                const question = rect('[data-exam-question]');
                const answers = rect('#question-answers');
                const label = document.querySelector('label[for="question1_A"]');
                return {
                    clearance: media.top - header.bottom,
                    questionClearance: question.top - media.bottom,
                    answerGap: answers.top - question.bottom,
                    questionOffset: question.left - context.left,
                    answerOffset: answers.left - question.left,
                    answerWidth: answers.width,
                    choiceHeight: label.getBoundingClientRect().height,
                    choiceAlignment: getComputedStyle(label).textAlign,
                    pageWidth: document.documentElement.scrollWidth,
                    viewportWidth: innerWidth
                };
            }
            """);
        Assert.True(metrics.GetProperty("clearance").GetDouble() >= 16, metrics.ToString());
        Assert.True(metrics.GetProperty("questionClearance").GetDouble() >= 16, metrics.ToString());
        Assert.InRange(metrics.GetProperty("answerGap").GetDouble(), 8, 24);
        Assert.InRange(Math.Abs(metrics.GetProperty("questionOffset").GetDouble()), 0, 1);
        Assert.InRange(Math.Abs(metrics.GetProperty("answerOffset").GetDouble()), 0, 1);
        Assert.InRange(metrics.GetProperty("answerWidth").GetDouble(), 200, 700);
        Assert.True(metrics.GetProperty("choiceHeight").GetDouble() >= 44);
        Assert.Equal("left", metrics.GetProperty("choiceAlignment").GetString());
        Assert.True(metrics.GetProperty("pageWidth").GetDouble() <= metrics.GetProperty("viewportWidth").GetDouble(), metrics.ToString());

        await SaveAsync(() => Page.Locator("label[for='question1_A']").ClickAsync());
        await Expect(Page.Locator("#question1_A")).ToBeCheckedAsync();
        await SaveAsync(() => Page.Locator("label[for='question1_A']").ClickAsync());
        await Expect(Page.Locator("#question1_A")).Not.ToBeCheckedAsync();
        await Page.Locator("#question1_A").FocusAsync();
        await SaveAsync(() => Page.Keyboard.PressAsync("Space"));
        await SaveAsync(() => Page.Keyboard.PressAsync("ArrowDown"));
        await Expect(Page.Locator("#question1_B")).ToBeCheckedAsync();
        await Expect(Page.Locator("#question1_B")).ToBeFocusedAsync();
        Assert.True(await Page.Locator("label[for='question1_B']").EvaluateAsync<bool>(
            "label => parseFloat(getComputedStyle(label).outlineWidth) >= 2 && getComputedStyle(label).outlineStyle !== 'none'"));
        await Page.EvaluateAsync("window.scrollTo(0, document.documentElement.scrollHeight)");
        await Expect(Page.Locator("label[for='question1_D']")).ToBeInViewportAsync();
        Assert.InRange(await Page.Locator("[data-exam-header]").EvaluateAsync<double>("header => header.getBoundingClientRect().top"), 0, 1);
        await Page.EvaluateAsync("window.scrollTo(0, 0)");
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"b22-{(portal ? "portal" : "public")}-{culture}-{width}.png"), FullPage = true });

        await GoToQuestionAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 2).Id);
        var openAnswer = Page.GetByRole(AriaRole.Textbox, new() { Name = "Explain your choice.", Exact = true });
        await Expect(openAnswer).ToBeVisibleAsync();
        await Expect(Page.Locator("#question-form audio")).ToHaveAttributeAsync("controls", "");
        await SaveAsync(() => openAnswer.FillAsync("I can contact my teacher to discuss the next lesson."));
        await GoToQuestionAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 3).Id);
        await Expect(Page.Locator("#question3_true")).ToBeAttachedAsync();
        await SaveAsync(() => Page.Locator("label[for='question3_true']").ClickAsync());
        await Expect(Page.Locator("#question3_true")).ToBeCheckedAsync();
        await GoToQuestionAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 1).Id);
        await Expect(Page.Locator("#question1_B")).ToBeCheckedAsync();
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.Locator("#question1_B")).ToBeCheckedAsync();
        await Page.GetByLabel(culture == "nl-nl" ? "Hulp bij de vraag" : "Question help", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Choose the most suitable response.", new() { Exact = true })).ToBeVisibleAsync();

        await using var db = LocalTestApplication.CreateDbContext();
        var saved = await db.ParticipantAnswers.Where(answer => answer.RecordId == data.Record.Id)
            .Select(answer => new { answer.Question.QuestionNumber, answer.AnswerText })
            .ToDictionaryAsync(answer => answer.QuestionNumber, answer => answer.AnswerText);
        Assert.Equal("B", saved[1]);
        Assert.Equal("I can contact my teacher to discuss the next lesson.", saved[2]);
        Assert.Equal("True", saved[3]);
        Assert.Equal(ExamStatus.InProgress, (await db.Records.SingleAsync(record => record.Id == data.Record.Id)).ExamStatus);
    }

    private async Task SaveAsync(Func<Task> action)
    {
        var response = await Page.RunAndWaitForResponseAsync(action,
            result => result.Request.Method == "POST" && result.Url.Contains("handler=SubmitAnswer", StringComparison.Ordinal));
        Assert.Equal(200, response.Status);
        Assert.True((await response.JsonAsync())!.Value.GetProperty("success").GetBoolean());
    }

    private Task GoToQuestionAsync(int questionId)
        => Page.Locator($"[data-action='load-question'][data-question-id='{questionId}']").ClickAsync();

    private sealed class TakingFixture(ApplicationDbContext db) : IAsyncDisposable
    {
        public Exam Exam { get; private set; } = null!;
        public iD_Develops.Models.Record Record { get; private set; } = null!;

        public static async Task<TakingFixture> CreateAsync(string userId, bool portal)
        {
            var fixture = new TakingFixture(LocalTestApplication.CreateDbContext());
            try { await fixture.SeedAsync(userId, portal); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task SeedAsync(string userId, bool portal)
        {
            Exam = new Exam
            {
                Name = "Language and communication for work, learning, and everyday conversations",
                CreatedByUserId = userId,
                PublishStatus = ExamPublishStatus.Published,
                TimeLimitSeconds = 900,
                IntroductionPrimaryLanguage = "Read each question and choose your answer.",
                Questions =
                [
                    new MultipleChoiceQuestion
                    {
                        QuestionNumber = 1,
                        Text = "<p>What is the most suitable response when you want to discuss your next lesson?</p>",
                        Scenario = "<p>Your teacher has sent you a message about your next lesson. Read the information and choose the response that best fits the situation.</p>",
                        ImageReference = $"{LocalTestApplication.PublicBaseUrl}/e2e-exam-context.svg",
                        MessageBeforeQuestion = "<p>Choose the most suitable response.</p>",
                        Score = 2,
                        CorrectAnswers = [new() { Text = "B" }],
                        MultipleChoiceAnswer = new()
                        {
                            AnswerA = "I will contact my teacher to discuss a suitable date and time for the next lesson.",
                            AnswerB = "Could we discuss the next lesson together? I would like to ask a few questions before we make an appointment.",
                            AnswerC = new string('W', 100),
                            AnswerD = "I do not need a reply."
                        }
                    },
                    new OpenQuestion { QuestionNumber = 2, Text = "Explain your choice.", AudioReference = $"{LocalTestApplication.PublicBaseUrl}/e2e-exam-audio.wav", Score = 2, CorrectAnswers = [new() { Text = "Contact my teacher" }] },
                    new TrueOrFalseQuestion { QuestionNumber = 3, Text = "Can you contact your teacher?", Score = 1, CorrectAnswers = [new() { Text = "True" }] }
                ]
            };
            db.Exams.Add(Exam);
            await db.SaveChangesAsync();
            var version = new ExamVersion
            {
                Exam = Exam,
                VersionNumber = 1,
                Name = Exam.Name,
                TimeLimitSeconds = 900,
                PublicSlug = portal ? null : "level-test",
                PublishedAtUtc = DateTime.UtcNow,
                Questions = Exam.Questions.Select(question => new ExamVersionQuestion
                {
                    SourceQuestionId = question.Id,
                    QuestionNumber = question.QuestionNumber,
                    QuestionType = question switch { MultipleChoiceQuestion => "MultipleChoice", TrueOrFalseQuestion => "TrueOrFalse", _ => "Open" },
                    Text = question.Text,
                    Scenario = question.Scenario,
                    ImageReference = question.ImageReference,
                    AudioReference = question.AudioReference,
                    MessageBeforeQuestion = question.MessageBeforeQuestion,
                    Score = question.Score,
                    AnswerA = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerA,
                    AnswerB = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerB,
                    AnswerC = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerC,
                    AnswerD = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerD,
                    CorrectAnswers = question.CorrectAnswers.Select(answer => new ExamVersionCorrectAnswer { Text = answer.Text }).ToList()
                }).ToList()
            };
            Record = new iD_Develops.Models.Record
            {
                Exam = Exam,
                ExamVersion = version,
                UserId = portal ? userId : null,
                StartDateTime = DateTime.UtcNow.AddSeconds(-10),
                EndDateTime = DateTime.UtcNow.AddSeconds(890),
                ExamStatus = ExamStatus.InProgress,
                StatusReason = RecordStatusReason.Started
            };
            db.Records.Add(Record);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Record != null)
                {
                    await db.ParticipantAnswers.Where(answer => answer.RecordId == Record.Id).ExecuteDeleteAsync();
                    await db.Records.Where(record => record.Id == Record.Id).ExecuteDeleteAsync();
                    await db.ApplicationAuditEvents.Where(audit => audit.EntityId == Record.Id.ToString()).ExecuteDeleteAsync();
                }
                if (Exam?.Id > 0)
                {
                    await db.ExamLogs.Where(log => log.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.Questions.Where(question => question.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.Exams.Where(exam => exam.Id == Exam.Id).ExecuteDeleteAsync();
                }
            }
            finally { await db.DisposeAsync(); }
        }
    }
}
