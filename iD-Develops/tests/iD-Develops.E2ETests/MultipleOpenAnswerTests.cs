using System.Text.Json;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Pages.Examination;
using iD_Develops.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class MultipleOpenAnswerTests : DiagnosticPageTest
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Context.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
    }

    [Theory]
    [InlineData("en-us", 1440)]
    [InlineData("nl-nl", 320)]
    public Task PublicInputsSurviveNavigationAndRefreshAndReceiveTheSourceAwards(string culture, int width)
        => VerifyInputsAsync(false, culture, width);

    [Fact]
    [Trait("SiteMode", "Full")]
    public Task StudentPortalInputsSurviveNavigationAndRefreshAndReceiveTheSourceAwards()
        => VerifyInputsAsync(true, "en-us", 390);

    private async Task VerifyInputsAsync(bool portal, string culture, int width)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await using var data = await AnswerFixture.CreateAsync(account.User.Id, portal);
        await Page.SetViewportSizeAsync(width, width < 500 ? 844 : 1000);
        var path = portal ? $"/examination/{data.Record.Id}" : $"/{culture}/examination/level-test/{data.Record.Id}";
        if (portal)
            await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password, path, Page.Locator("#question-form"));
        else
            Assert.Equal(200, (await Page.GotoAsync(LocalTestApplication.PublicBaseUrl + path, new() { WaitUntil = WaitUntilState.DOMContentLoaded }))!.Status);
        var first = Page.GetByLabel("First missing word", new() { Exact = true });
        var second = Page.GetByLabel("Second missing word", new() { Exact = true });
        await Expect(first).ToBeVisibleAsync();
        await Expect(second).ToBeVisibleAsync();
        Assert.DoesNotContain("AcceptedAnswers", await Page.Locator("#question-form").InnerHTMLAsync());
        await SaveAsync(() => first.FillAsync("om"));
        var missing = await Page.RunAndWaitForResponseAsync(() => Page.Locator("#submitExam").ClickAsync(),
            response => response.Url.Contains("handler=ValidateMissingQuestions", StringComparison.Ordinal));
        var missingNumbers = (await missing.JsonAsync())!.Value.GetProperty("missingQuestionNumbers").EnumerateArray().Select(number => number.GetInt32()).ToList();
        Assert.Contains(1, missingNumbers);
        await Expect(Page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(first).ToHaveValueAsync("om");
        await Expect(second).ToHaveValueAsync("");
        await SaveAsync(() => first.FillAsync(""));
        await using (var db = LocalTestApplication.CreateDbContext())
            Assert.False(await db.ParticipantAnswers.AnyAsync(answer => answer.RecordId == data.Record.Id && !answer.IsDeleted));
        await SaveAsync(() => first.FillAsync("OM"));
        await first.PressAsync("Tab");
        await Expect(second).ToBeFocusedAsync();
        await SaveAsync(() => second.FillAsync("te"));
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        Assert.True(await first.EvaluateAsync<bool>("element => parseFloat(getComputedStyle(element).fontSize) >= 16"));
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"open-answer-taking-{width}.png"), FullPage = true });

        await NavigateAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 2).Id);
        await SaveAsync(() => Page.GetByLabel("Compound word", new() { Exact = true }).FillAsync("zonnebril"));
        await NavigateAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 3).Id);
        await SaveAsync(() => Page.GetByRole(AriaRole.Textbox, new() { Name = "Legacy city answer", Exact = true }).FillAsync("Amsterdam"));
        await NavigateAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 4).Id);
        await Expect(Page.GetByRole(AriaRole.Radio)).ToHaveCountAsync(2);
        await SaveAsync(() => Page.GetByText("Correct option", new() { Exact = true }).ClickAsync());
        await NavigateAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 1).Id);
        await Expect(first).ToHaveValueAsync("OM");
        await Expect(second).ToHaveValueAsync("te");
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(first).ToHaveValueAsync("OM");
        await Expect(second).ToHaveValueAsync("te");
        await Page.Locator("#submitExam").ClickAsync();
        await Page.WaitForURLAsync($"**/examination/completed/{data.Record.Id}");
        await using (var db = LocalTestApplication.CreateDbContext())
            Assert.Equal(3.5, (await db.Records.AsNoTracking().SingleAsync(record => record.Id == data.Record.Id)).Score);
        Assert.Equal(200, (await Page.GotoAsync(data.ResultsUrl(culture), new() { WaitUntil = WaitUntilState.DOMContentLoaded }))!.Status);
        await Expect(Page.Locator("[data-question-number='1'] dd").First).ToContainTextAsync("First missing word: OM");
        await Expect(Page.Locator("[data-question-number='1'] dd").First).ToContainTextAsync("Second missing word: te");
        await Expect(Page.Locator("[data-question-number='1'] dd").Last).ToContainTextAsync("First missing word: om");
        await Expect(Page.Locator("[data-question-number='1'] [data-question-points]")).ToHaveTextAsync(culture == "nl-nl" ? "1 / 1 punten" : "1 / 1 points");
        await Expect(Page.Locator("[data-question-number='1'] [data-question-result]")).ToHaveTextAsync(culture == "nl-nl" ? "Goed" : "Correct");
        await Expect(Page.Locator("[data-question-number='2'] [data-question-result]")).ToHaveTextAsync(culture == "nl-nl" ? "Gedeeltelijk goed" : "Partially correct");
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(320)]
    public async Task EditorAddsReordersAndRemovesInputsAndPreservesAlternativesAfterReload(int width)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var data = await AnswerFixture.CreateAsync(account.User.Id, false);
        await Page.SetViewportSizeAsync(width, width < 500 ? 844 : 1000);
        var questionId = data.Exam.Questions.Single(question => question.QuestionNumber == 1).Id;
        await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password,
            $"/exams/{data.Exam.Id}/edit?questionId={questionId}", Page.Locator("[data-question-edit-form]"));
        var editor = Page.Locator("[data-open-answer-editor]");
        await Expect(editor).ToHaveAttributeAsync("data-open-editor-ready", "true");
        await Page.WaitForFunctionAsync("() => !!window.examEditor");
        var fields = editor.Locator("[data-open-fields] > [data-open-field]");
        await Expect(fields).ToHaveCountAsync(2);
        await EditAsync(() => fields.First.GetByRole(AriaRole.Textbox, new() { Name = "Input label (optional)", Exact = true }).FillAsync("Before the infinitive"));
        await EditAsync(() => editor.GetByRole(AriaRole.Button, new() { Name = "Add answer input", Exact = true }).ClickAsync());
        await Expect(fields).ToHaveCountAsync(3);
        await Expect(fields.Last.Locator("[data-open-label]")).ToBeFocusedAsync();
        await EditAsync(() => fields.Last.Locator("[data-open-label]").FillAsync("Third blank"));
        await fields.Last.GetByRole(AriaRole.Button, new() { Name = "Add accepted answer", Exact = true }).ClickAsync();
        await EditAsync(() => fields.Last.GetByRole(AriaRole.Textbox, new() { Name = "Accepted answer 1", Exact = true }).FillAsync("extra"));
        await EditAsync(() => fields.Last.GetByRole(AriaRole.Button, new() { Name = "Move answer input up", Exact = true }).ClickAsync());
        await Expect(fields.Nth(1).Locator("[data-open-label]")).ToHaveValueAsync("Third blank");
        await EditAsync(() => fields.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Remove input", Exact = true }).ClickAsync());
        await Expect(fields).ToHaveCountAsync(2);
        await EditAsync(() => fields.Last.GetByRole(AriaRole.Button, new() { Name = "Move answer input up", Exact = true }).ClickAsync());
        await fields.First.GetByRole(AriaRole.Button, new() { Name = "Add accepted answer", Exact = true }).ClickAsync();
        await EditAsync(() => fields.First.GetByRole(AriaRole.Textbox, new() { Name = "Accepted answer 2", Exact = true }).FillAsync("alternative"));
        var points = fields.First.GetByRole(AriaRole.Spinbutton, new() { Name = "Points awarded", Exact = true }).Last;
        await points.FillAsync("-1");
        await Expect(points).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(fields.First.Locator("[data-open-award-error]").Last).ToContainTextAsync("zero or greater");
        await Expect(Page.Locator("#editorSaveStatus")).ToContainTextAsync("Save failed");
        // Bypass client validation to verify that the server independently rejects the award.
        Assert.Equal(400, await PostEditorFormAsync());
        await using (var persisted = LocalTestApplication.CreateDbContext())
        {
            var unchanged = await persisted.OpenQuestions.AsNoTracking().SingleAsync(question => question.Id == questionId);
            Assert.Equal(1, unchanged.OpenAnswerFields[0].AcceptedAnswers.Single(answer => answer.Text == "alternative").Score);
        }
        await EditAsync(() => points.FillAsync("0.5"));
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(editor).ToHaveAttributeAsync("data-open-editor-ready", "true");
        await Expect(fields).ToHaveCountAsync(2);
        await Expect(fields.First.Locator("[data-open-label]")).ToHaveValueAsync("Second missing word");
        await Expect(fields.Last.Locator("[data-open-label]")).ToHaveValueAsync("Before the infinitive");
        await Expect(fields.First.GetByRole(AriaRole.Textbox, new() { Name = "Accepted answer 2", Exact = true })).ToHaveValueAsync("alternative");
        await Expect(fields.First.GetByRole(AriaRole.Spinbutton, new() { Name = "Points awarded", Exact = true }).Last).ToHaveValueAsync("0.5");
        Assert.True(await editor.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await fields.First.ScrollIntoViewIfNeededAsync();
        await Expect(fields.First.Locator("[data-open-label]")).ToBeInViewportAsync();
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"open-answer-editor-{width}.png") });
        await using var db = LocalTestApplication.CreateDbContext();
        var saved = await db.OpenQuestions.AsNoTracking().SingleAsync(question => question.Id == questionId);
        Assert.Equal(new[] { "second", "first" }, saved.OpenAnswerFields.Select(field => field.Key));
        Assert.Equal(0.5, saved.OpenAnswerFields[0].AcceptedAnswers.Single(answer => answer.Text == "alternative").Score);
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(320)]
    public async Task AllQuestionTypesUseOneMaximumAndExplainTheirAwards(int width)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var data = await AnswerFixture.CreateAsync(account.User.Id, false);
        var trueFalse = new TrueOrFalseQuestion { ExamId = data.Exam.Id, QuestionNumber = 5, Text = "True or false?", Score = 1, CorrectAnswers = [new() { Text = "True" }] };
        await using (var setup = LocalTestApplication.CreateDbContext())
        {
            setup.Questions.Add(trueFalse);
            await setup.SaveChangesAsync();
        }
        await Page.SetViewportSizeAsync(width, width < 500 ? 844 : 1000);
        var open = data.Exam.Questions.Single(question => question.QuestionNumber == 1);
        await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password,
            $"/exams/{data.Exam.Id}/edit?questionId={open.Id}", Page.Locator("[data-question-edit-form]"));
        foreach (var id in new[] { open.Id, data.Exam.Questions.Single(question => question.QuestionNumber == 4).Id, trueFalse.Id })
        {
            if (id != open.Id)
            {
                await Page.Locator($"[data-action='load-question'][data-question-id='{id}']").ClickAsync();
                await Expect(Page.Locator("[data-question-edit-form] input[name='QuestionId']")).ToHaveValueAsync(id.ToString());
            }
            var panel = Page.Locator("[data-question-scoring]");
            await Expect(panel).ToHaveAttributeAsync("data-scoring-ready", "true");
            var maximum = Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Maximum points", Exact = true });
            await Expect(maximum).ToHaveCountAsync(1);
            await Expect(maximum).ToHaveValueAsync("1");
            await Expect(panel).ToContainTextAsync("This maximum counts once toward the exam total.");
            if (id == open.Id)
            {
                await Expect(maximum).ToHaveAttributeAsync("readonly", "");
                await Expect(panel).ToContainTextAsync("Calculated from the highest award in each answer input.");
                var award = Page.Locator("[data-open-field]").First.GetByRole(AriaRole.Spinbutton, new() { Name = "Points awarded", Exact = true });
                await EditAsync(() => award.FillAsync("2"));
                await Expect(maximum).ToHaveValueAsync("2.5");
                await Expect(Page.Locator("[data-open-group-maximum]").First).ToContainTextAsync("2 points");
            }
            else
            {
                await maximum.FillAsync("0");
                await Expect(maximum).ToHaveAttributeAsync("aria-invalid", "true");
                await Expect(panel.Locator("[data-question-points-error]")).ToBeVisibleAsync();
                await Expect(Page.Locator("#editorSaveStatus")).ToContainTextAsync("Save failed");
                Assert.Equal(400, await PostEditorFormAsync());
                await EditAsync(() => maximum.FillAsync("2"));
                await Expect(panel.Locator("[data-question-award-summary]")).ToHaveTextAsync("Correct: 2 points. Incorrect or unanswered: 0.");
            }
            await Expect(Page.Locator("#editorSaveStatus")).ToContainTextAsync("Up-to-date");
            await panel.EvaluateAsync("element => element.scrollIntoView({ block: 'center' })");
            Assert.True(await panel.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
            await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"question-scoring-{id == open.Id}-{width}.png") });
        }
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Maximum points", Exact = true })).ToHaveValueAsync("2");
        await using var db = LocalTestApplication.CreateDbContext();
        var saved = await db.Questions.AsNoTracking().Where(question => question.ExamId == data.Exam.Id).ToListAsync();
        Assert.Equal(2.5, saved.Single(question => question.Id == open.Id).Score);
        Assert.Equal(2, saved.Single(question => question.Id == trueFalse.Id).Score);
        Assert.Equal(8.5, ExamScoring.TotalMaximum(saved));
    }

    [Theory]
    [InlineData("en-us", 1440, "om", "te", 1, "Correct")]
    [InlineData("nl-nl", 320, "om", "te", 1, "Goed")]
    [InlineData("en-us", 320, "om", "", 0.5, "Partially correct")]
    [InlineData("nl-nl", 1440, "wrong", "te", 0.5, "Gedeeltelijk goed")]
    [InlineData("en-us", 1440, "wrong", "wrong", 0, "Incorrect")]
    public async Task ExistingSnapshotResultsShowIndependentInputCreditAndIgnoreLiveAnswerEdits(
        string culture, int width, string first, string second, double expectedScore, string expectedLabel)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var data = await AnswerFixture.CreateAsync(account.User.Id, false);
        var questionId = data.Exam.Questions.Single(question => question.QuestionNumber == 1).Id;
        await using (var db = LocalTestApplication.CreateDbContext())
        {
            await db.Records.Where(record => record.Id == data.Record.Id).ExecuteUpdateAsync(update =>
                update.SetProperty(record => record.ExamStatus, ExamStatus.Completed)
                    .SetProperty(record => record.EndDateTime, DateTime.UtcNow));
            db.ParticipantAnswers.Add(new()
            {
                QuestionId = questionId,
                RecordId = data.Record.Id,
                AnswerText = System.Text.Json.JsonSerializer.Serialize(new { format = "open-answers-v1", answers = new { first, second } })
            });
            var liveQuestion = await db.OpenQuestions.SingleAsync(question => question.Id == questionId);
            liveQuestion.OpenAnswerFields[0].AcceptedAnswers = [new() { Text = "changed live key", Score = 9 }];
            await db.SaveChangesAsync();
        }
        await Page.SetViewportSizeAsync(width, 1000);
        Assert.Equal(200, (await Page.GotoAsync(data.ResultsUrl(culture), new() { WaitUntil = WaitUntilState.DOMContentLoaded }))!.Status);
        var review = Page.Locator("[data-question-number='1']");
        await Expect(review.Locator("[data-question-result]")).ToHaveTextAsync(expectedLabel);
        var score = expectedScore.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo(culture));
        await Expect(review.Locator("[data-question-points]")).ToHaveTextAsync($"{score} / 1 {(culture == "nl-nl" ? "punten" : "points")}");
        await Expect(review.Locator("dd").Last).ToContainTextAsync("om (0.5)");
        Assert.DoesNotContain("changed live key", await review.InnerTextAsync());
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"open-answer-credit-{culture}-{width}-{expectedScore}.png"), FullPage = true });
    }

    private Task<int> PostEditorFormAsync() => Page.EvaluateAsync<int>("""
        async () => {
            const form = document.querySelector('[data-question-edit-form]');
            const body = new FormData(form);
            body.set('examId', document.querySelector('#ExamId').value);
            const response = await fetch(location.pathname + '?handler=SaveQuestion', { method: 'POST', body });
            return response.status;
        }
        """);

    private async Task SaveAsync(Func<Task> action)
    {
        var response = await Page.RunAndWaitForResponseAsync(action,
            response => response.Request.Method == "POST" && response.Url.Contains("handler=SubmitAnswer", StringComparison.Ordinal));
        Assert.Equal(200, response.Status);
        Assert.True((await response.JsonAsync())!.Value.GetProperty("success").GetBoolean());
    }
    private async Task<IResponse> EditAsync(Func<Task> action, int expectedStatus = 200)
    {
        var response = await Page.RunAndWaitForResponseAsync(action,
            response => response.Request.Method == "POST" && response.Url.Contains("handler=SaveQuestion", StringComparison.Ordinal));
        Assert.Equal(expectedStatus, response.Status);
        return response;
    }
    private async Task NavigateAsync(int questionId)
    {
        await Page.Locator($"[data-action='load-question'][data-question-id='{questionId}']").ClickAsync();
        await Expect(Page.Locator("#questionId")).ToHaveValueAsync(questionId.ToString());
    }

    private sealed class AnswerFixture(ApplicationDbContext db) : IAsyncDisposable
    {
        public Exam Exam { get; private set; } = null!;
        public iD_Develops.Models.Record Record { get; private set; } = null!;
        public static async Task<AnswerFixture> CreateAsync(string userId, bool portal)
        {
            var fixture = new AnswerFixture(LocalTestApplication.CreateDbContext());
            try { await fixture.SeedAsync(userId, portal); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }
        private async Task SeedAsync(string userId, bool portal)
        {
            Exam = new Exam
            {
                Name = $"E2E multi-input {Guid.NewGuid():N}",
                CreatedByUserId = userId,
                IntroductionPrimaryLanguage = "Complete the answers.",
                PublishStatus = ExamPublishStatus.Published,
                TimeLimitSeconds = 900,
                Questions =
                [
                    new OpenQuestion { QuestionNumber = 1, Text = "Complete both missing words.", Scenario = "Ik vind het leuk ____ een nieuwe taal ____ leren.", Score = 1,
                        OpenAnswerFields = [new() { Key = "first", Label = "First missing word", AcceptedAnswers = [new() { Text = "om", Score = 0.5 }] },
                            new() { Key = "second", Label = "Second missing word", AcceptedAnswers = [new() { Text = "te", Score = 0.5 }] }] },
                    new OpenQuestion { QuestionNumber = 2, Text = "Name the compound word, including its article.", Score = 1,
                        OpenAnswerFields = [new() { Key = "word", Label = "Compound word", AcceptedAnswers = [new() { Text = "de zonnebril" }, new() { Text = "een zonnebril" }, new() { Text = "zonnebril", Score = 0.5 }] }] },
                    new OpenQuestion { QuestionNumber = 3, Text = "Legacy city answer", Score = 1, CorrectAnswers = [new() { Text = "Amsterdam" }] },
                    new MultipleChoiceQuestion { QuestionNumber = 4, Text = "Choose the correct option.", Score = 1,
                        MultipleChoiceAnswer = new() { AnswerA = "Wrong option", AnswerB = "Correct option" }, CorrectAnswers = [new() { Text = "B" }] }
                ]
            };
            db.Exams.Add(Exam);
            await db.SaveChangesAsync();
            var version = new ExamVersion
            {
                Exam = Exam,
                VersionNumber = 1,
                Name = Exam.Name,
                IntroductionPrimaryLanguage = Exam.IntroductionPrimaryLanguage,
                TimeLimitSeconds = 900,
                PublicSlug = "level-test",
                PublishedAtUtc = DateTime.UtcNow,
                Questions = Exam.Questions.Select(question => new ExamVersionQuestion
                {
                    SourceQuestionId = question.Id,
                    QuestionType = question is MultipleChoiceQuestion ? "MultipleChoice" : "Open",
                    QuestionNumber = question.QuestionNumber,
                    Text = question.Text,
                    Scenario = question.Scenario,
                    Score = question.Score,
                    OpenAnswerFields = question is OpenQuestion open ? OpenAnswerFieldJson.Copy(open.OpenAnswerFields) : [],
                    AnswerA = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerA,
                    AnswerB = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerB,
                    CorrectAnswers = question.CorrectAnswers.Select(answer => new ExamVersionCorrectAnswer { Text = answer.Text }).ToList()
                }).ToList()
            };
            Record = new()
            {
                Exam = Exam,
                ExamVersion = version,
                UserId = portal ? userId : null,
                StartDateTime = DateTime.UtcNow.AddSeconds(-5),
                EndDateTime = DateTime.UtcNow.AddSeconds(895),
                ExamStatus = ExamStatus.InProgress,
                StatusReason = RecordStatusReason.Started
            };
            db.Records.Add(Record);
            await db.SaveChangesAsync();
        }
        public string ResultsUrl(string culture)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(LocalTestApplication.DatabaseConnectionString));
            services.AddDataProtection().SetApplicationName("iD-Develops").PersistKeysToDbContext<ApplicationDbContext>();
            using var provider = services.BuildServiceProvider();
            var token = ResultsModel.CreatePublicResultToken(provider.GetRequiredService<IDataProtectionProvider>(), Record.Id, "open-answers@example.test");
            return $"{LocalTestApplication.PublicBaseUrl}/{culture}/examination/results/{Record.Id}?token={Uri.EscapeDataString(token)}";
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
