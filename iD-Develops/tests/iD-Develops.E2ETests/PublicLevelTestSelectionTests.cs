using System.Net;
using System.Text.RegularExpressions;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

[CollectionDefinition("Public level-test configuration", DisableParallelization = true)]
public sealed class PublicLevelTestConfigurationCollection;

[Collection("Public level-test configuration")]
public sealed class PublicLevelTestSelectionTests : DiagnosticPageTest
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Context.RouteAsync("https://consent.cookiebot.com/**", route => route.AbortAsync());
    }

    [Theory]
    [InlineData("en-us", 1440)]
    [InlineData("en-us", 320)]
    [InlineData("nl-nl", 1440)]
    [InlineData("nl-nl", 320)]
    public async Task BothChoicesStartTheirOwnExamAndEmailTheirOwnProtectedResults(string culture, int width)
    {
        await using var fixture = await SelectionFixture.CreateAsync();
        await Page.SetViewportSizeAsync(width, width < 500 ? 844 : 1000);
        foreach (var level in new[] { "a1", "a2" })
        {
            var exam = fixture.Exams.Single(exam => exam.PublicSlug == PublicLevelTests.SlugForLevel(level));
            await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/level-test", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(Page.Locator("[data-level-test-choice]")).ToHaveCountAsync(2);
            await Expect(Page.Locator($"[data-level-test-choice='{level}'] a")).ToBeVisibleAsync();
            Assert.Equal(0, await fixture.RecordCountAsync(exam.Id));
            Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
            if (level == "a1")
                await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"level-test-selection-{culture}-{width}.png"), FullPage = true });
            await Page.Locator($"[data-level-test-choice='{level}'] a").ClickAsync();
            await Expect(Page.Locator("[data-selected-level]")).ToHaveAttributeAsync("data-selected-level", level);
            await Expect(Page.Locator("[data-test-introduction]")).ToHaveTextAsync($"{(culture == "nl-nl" ? "Nederlandse" : "English")} introduction {level}");
            Assert.Equal(0, await fixture.RecordCountAsync(exam.Id));
            var start = Page.GetByRole(AriaRole.Button, new() { Name = culture == "nl-nl" ? $"Start {level.ToUpperInvariant()}-test" : $"Start {level.ToUpperInvariant()} test", Exact = true });
            await start.ClickAsync();
            await Expect(Page.Locator("#question-form")).ToBeVisibleAsync();
            var recordId = Guid.Parse((await Page.Locator("#RecordId").GetAttributeAsync("value"))!);
            await using (var db = LocalTestApplication.CreateDbContext())
            {
                var record = await db.Records.AsNoTracking().SingleAsync(record => record.Id == recordId);
                Assert.Equal(exam.Id, record.ExamId);
                Assert.Equal(TimeSpan.FromSeconds(exam.TimeLimitSeconds!.Value), record.EndDateTime - record.StartDateTime);
            }
            if (level == "a1")
                await SaveAnswerAsync(() => Page.GetByText("Amsterdam", new() { Exact = true }).ClickAsync());
            else
            {
                await SaveAnswerAsync(() => Page.GetByLabel("First word", new() { Exact = true }).FillAsync("om"));
                await SaveAnswerAsync(() => Page.GetByLabel("Second word", new() { Exact = true }).FillAsync("te"));
            }
            await Page.Locator("#submitExam").ClickAsync();
            await Page.WaitForURLAsync($"**/examination/completed/{recordId}");
            await Expect(Page.Locator("[data-completed-test]")).ToHaveTextAsync(exam.Name);
            var email = $"level-selection-{Guid.NewGuid():N}@example.test";
            var directory = FileMailDirectory();
            try
            {
                await Page.Locator("#Name").FillAsync("Level selection learner");
                await Page.Locator("#Email").FillAsync(email);
                await Page.GetByRole(AriaRole.Button, new() { Name = culture == "nl-nl" ? "Stuur mijn resultaat!" : "Send my results!", Exact = true }).ClickAsync();
                await Expect(Page.Locator("#Email")).ToHaveCountAsync(0);
                var body = FileMailTestStore.Matching(directory, $"To: {email}").Select(mail => mail.Body).Single();
                Assert.Contains(exam.Name, body);
                Assert.Contains($"{level.ToUpperInvariant()} {(culture == "nl-nl" ? "advies" : "guidance")}", body);
                var link = Regex.Matches(body, "href=\"([^\"]+)\"").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
                    .First(url => url.Contains($"/examination/results/{recordId}", StringComparison.Ordinal));
                Assert.StartsWith(LocalTestApplication.PublicBaseUrl + "/" + culture + "/", link, StringComparison.Ordinal);
                // Historical attempts must remain readable after the live designation changes.
                await using (var db = LocalTestApplication.CreateDbContext())
                    await db.Exams.Where(item => item.Id == exam.Id).ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.PublicSlug, (string?)null).SetProperty(item => item.PublishStatus, ExamPublishStatus.Archived));
                await Page.GotoAsync(link, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = exam.Name, Exact = true })).ToBeVisibleAsync();
                await Expect(Page.GetByText($"{level.ToUpperInvariant()} {(culture == "nl-nl" ? "advies" : "guidance")}", new() { Exact = true })).ToBeVisibleAsync();
                await Expect(Page.GetByText($"{level} completion", new() { Exact = true })).ToBeVisibleAsync();
                Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
            }
            finally
            {
                FileMailTestStore.DeleteMatching(directory, $"To: {email}");
                await using var cleanup = LocalTestApplication.CreateDbContext();
                await cleanup.Prospects.Where(prospect => prospect.Email == email).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task UnavailableAndForgedSelectionsDoNotCreateAttemptsOrDisableTheOtherLevel()
    {
        await using var fixture = await SelectionFixture.CreateAsync();
        await fixture.Db.Exams.Where(exam => exam.PublicSlug == PublicLevelTests.A2Slug)
            .ExecuteUpdateAsync(update => update.SetProperty(exam => exam.PublishStatus, ExamPublishStatus.Draft));
        await Page.GotoAsync(LocalTestApplication.PublicBaseUrl + "/en-us/level-test");
        await Expect(Page.Locator("[data-level-test-choice='a1'] a")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-level-test-choice='a2'] button")).ToBeDisabledAsync();
        await Page.Locator("[data-level-test-choice='a1'] a").ClickAsync();
        await Expect(Page.Locator("[data-selected-level]")).ToHaveAttributeAsync("data-selected-level", "a1");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Start A1 test", Exact = true })).ToBeVisibleAsync();
        foreach (var level in new[] { "a2", "private-exam", "123" })
        {
            var status = await Page.EvaluateAsync<int>("""
                async level => {
                    const body = new FormData(document.querySelector('form[method="post"]'));
                    const response = await fetch(`/en-us/level-test/${level}?handler=CreateRecord`, { method: 'POST', body });
                    return response.status;
                }
                """, level);
            Assert.Equal(404, status);
        }
        Assert.Equal(0, await fixture.Db.Records.CountAsync(record => fixture.Exams.Select(exam => exam.Id).Contains(record.ExamId)));
    }

    [Fact]
    public async Task AdminSettingsShowTheSelectedLevelAndRejectAnOccupiedLevel()
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var fixture = await SelectionFixture.CreateAsync();
        var exam = fixture.Exams[1];
        await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password,
            $"/examination/settings?examId={exam.Id}", Page.Locator("#Settings_PublicLevelTestLevel"));
        var selector = Page.GetByLabel("Public test level", new() { Exact = true });
        await Expect(selector).ToHaveValueAsync("a2");
        await selector.SelectOptionAsync("a1");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save settings", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Another exam is already configured as the public A1 test.", new() { Exact = false })).ToBeVisibleAsync();
        Assert.Equal(PublicLevelTests.A2Slug, await fixture.Db.Exams.Where(item => item.Id == exam.Id).Select(item => item.PublicSlug).SingleAsync());
        await selector.SelectOptionAsync("a2");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save settings", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Settings saved.", new() { Exact = false })).ToBeVisibleAsync();
    }

    private async Task SaveAnswerAsync(Func<Task> action)
    {
        var response = await Page.RunAndWaitForResponseAsync(action, response => response.Request.Method == "POST" && response.Url.Contains("handler=SubmitAnswer", StringComparison.Ordinal));
        Assert.Equal(200, response.Status);
    }

    private static string FileMailDirectory()
    {
        _ = LocalTestApplication.DatabaseConnectionString;
        var path = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY");
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new InvalidOperationException("Local File mail pickup is required.");
        return path;
    }

    internal sealed class SelectionFixture(ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db => db;
        public List<Exam> Exams { get; } = [];
        private readonly List<(int Id, string Slug)> _original = [];
        public static async Task<SelectionFixture> CreateAsync()
        {
            var fixture = new SelectionFixture(LocalTestApplication.CreateDbContext());
            try { await fixture.SeedAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }
        private async Task SeedAsync()
        {
            var originals = await db.Exams.AsNoTracking().Where(exam => exam.PublicSlug == PublicLevelTests.A1Slug || exam.PublicSlug == PublicLevelTests.A2Slug).ToListAsync();
            _original.AddRange(originals.Select(exam => (exam.Id, exam.PublicSlug!)));
            foreach (var item in _original)
                await db.Exams.Where(exam => exam.Id == item.Id).ExecuteUpdateAsync(update => update.SetProperty(exam => exam.PublicSlug, (string?)null));
            foreach (var level in new[] { "a1", "a2" })
            {
                Question question = level == "a1"
                    ? new MultipleChoiceQuestion { MultipleChoiceAnswer = new() { AnswerA = "Amsterdam", AnswerB = "Rotterdam" }, CorrectAnswers = [new() { Text = "A" }] }
                    : new OpenQuestion { OpenAnswerFields = [new() { Key = "first", Label = "First word", AcceptedAnswers = [new() { Text = "om" }] }, new() { Key = "second", Label = "Second word", AcceptedAnswers = [new() { Text = "te" }] }] };
                question.QuestionNumber = 1;
                question.Text = level == "a1" ? "Choose the city." : "Complete both missing words.";
                question.Score = level == "a1" ? 1 : 2;
                Exams.Add(new Exam
                {
                    Name = $"{level.ToUpperInvariant()} selection {Guid.NewGuid():N}",
                    CreatedByUserId = "e2e-level-selection",
                    PublishStatus = ExamPublishStatus.Published,
                    PublicSlug = PublicLevelTests.SlugForLevel(level),
                    TimeLimitSeconds = level == "a1" ? 90 : 240,
                    DifficultyValue = level == "a1" ? 0 : 1,
                    IntroductionPrimaryLanguage = $"English introduction {level}",
                    IntroductionSecondaryLanguage = $"Nederlandse introduction {level}",
                    CompletionTextPrimary = $"{level} completion",
                    CompletionTextSecondary = $"{level} completion",
                    ResultGradeDisplayMode = ResultGradeDisplayMode.GradeBands,
                    GradeBands = [new() { MinimumScore = 0, LabelPrimary = $"Onder {level}", LabelSecondary = $"Below {level}" }, new() { MinimumScore = 1, LabelPrimary = $"{level.ToUpperInvariant()} advies", LabelSecondary = $"{level.ToUpperInvariant()} guidance" }],
                    Questions = [question]
                });
            }
            db.Exams.AddRange(Exams);
            await db.SaveChangesAsync();
        }
        public Task<int> RecordCountAsync(int examId) => db.Records.CountAsync(record => record.ExamId == examId);
        public async ValueTask DisposeAsync()
        {
            try
            {
                var ids = Exams.Where(exam => exam.Id > 0).Select(exam => exam.Id).ToList();
                var records = await db.Records.Where(record => ids.Contains(record.ExamId)).Select(record => record.Id).ToListAsync();
                await db.ParticipantAnswers.Where(answer => records.Contains(answer.RecordId)).ExecuteDeleteAsync();
                await db.Records.Where(record => records.Contains(record.Id)).ExecuteDeleteAsync();
                await db.ApplicationAuditEvents.Where(audit => audit.EntityId != null && records.Select(id => id.ToString()).Contains(audit.EntityId)).ExecuteDeleteAsync();
                await db.ExamLogs.Where(log => ids.Contains(log.ExamId)).ExecuteDeleteAsync();
                await db.Questions.Where(question => question.ExamId.HasValue && ids.Contains(question.ExamId.Value)).ExecuteDeleteAsync();
                await db.Exams.Where(exam => ids.Contains(exam.Id)).ExecuteDeleteAsync();
            }
            finally
            {
                try
                {
                    foreach (var item in _original)
                        await db.Exams.Where(exam => exam.Id == item.Id && exam.PublicSlug == null).ExecuteUpdateAsync(update => update.SetProperty(exam => exam.PublicSlug, item.Slug));
                }
                finally { await db.DisposeAsync(); }
            }
        }
    }
}
