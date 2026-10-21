using System.Text.RegularExpressions;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.AspNetCore.Identity;

namespace iD_Develops.E2ETests;

public sealed class ExamTimingPublicationTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task ConcurrentStarts_PreserveOneSharedVersionForTheSavedDefinition()
    {
        await using var teacher = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var data = await TimingFixture.CreateAsync(teacher.User.Id);
        await data.Db.Exams.Where(exam => exam.Id == data.Exam.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(exam => exam.TimeLimitSeconds, 2400));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(LocalTestApplication.DatabaseConnectionString, options => options.EnableRetryOnFailure()).Options);
            await ready.Task;
            return await new ExamVersionService(context).GetOrCreateAttemptVersionAsync(data.Exam.Id);
        }).ToArray();
        ready.SetResult();
        var versions = await Task.WhenAll(tasks);
        Assert.All(versions, version => Assert.Equal(2400, version!.DurationSeconds));
        Assert.Single(versions.Select(version => version!.ExamVersionId).Distinct());
        Assert.Equal(2, await data.Db.ExamVersions.CountAsync(version => version.ExamId == data.Exam.Id));
    }

    [Fact]
    public async Task TeacherLibrary_ScopesArchivesAndReadOnlyVersionsToOwnedExams()
    {
        await using var teacher = await CatalogBrowsingFixture.CreateAsync();
        await using var other = await CatalogBrowsingFixture.CreateAsync();
        await using (var setup = LocalTestApplication.CreateDbContext())
        {
            var teacherRole = await setup.Roles.SingleAsync(role => role.NormalizedName == "TEACHER");
            await setup.UserRoles.Where(role => role.UserId == teacher.User.Id).ExecuteDeleteAsync();
            setup.UserRoles.Add(new IdentityUserRole<string> { UserId = teacher.User.Id, RoleId = teacherRole.Id });
            await setup.SaveChangesAsync();
        }
        await using var owned = await TimingFixture.CreateAsync(teacher.User.Id);
        await using var foreign = await TimingFixture.CreateAsync(other.User.Id);
        owned.Exam.TimeLimitSeconds = 900;
        await owned.Db.SaveChangesAsync();
        await new ExamVersionService(owned.Db).GetOrCreateAttemptVersionAsync(owned.Exam.Id);
        owned.Exam.PublishStatus = ExamPublishStatus.Archived;
        await owned.Db.SaveChangesAsync();

        await LoginAsync(teacher, "/exams");
        await Expect(Page.GetByText(owned.Exam.Name, new() { Exact = true })).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Archived", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = owned.Exam.Name })).ToBeVisibleAsync();
        await Expect(Page.GetByText(foreign.Exam.Name, new() { Exact = true })).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Snapshots / Old versions", Exact = true }).ClickAsync();
        var rows = Page.GetByRole(AriaRole.Table, new() { Name = "Exam versions", Exact = true })
            .GetByRole(AriaRole.Row).Filter(new() { HasText = owned.Exam.Name });
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(Page.GetByText(foreign.Exam.Name, new() { Exact = true })).ToHaveCountAsync(0);
        var original = rows.Filter(new() { HasText = "v1" });
        await original.GetByRole(AriaRole.Link, new() { Name = "View version", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"{owned.Exam.Name} · v1", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("00:35:00", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Amsterdam · Default points", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToHaveCountAsync(0);
        var response = await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/exams/version?examId={foreign.Exam.Id}&versionId={foreign.Original.ExamVersionId}");
        Assert.True(response?.Status == 403 || Page.Url.Contains("/access-denied", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2100, 15)]
    [InlineData(2100, null)]
    [InlineData(null, 15)]
    public async Task SaveSettings_AppliesDurationToNewAttemptsWithoutRepublishing(int? originalSeconds, int? seconds)
    {
        await using var teacher = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var data = await TimingFixture.CreateAsync(teacher.User.Id, originalSeconds);
        await Page.SetViewportSizeAsync(1440, 1000);
        var settingsPath = $"/examination/settings?examId={data.Exam.Id}";
        await LoginAsync(teacher, settingsPath);

        var fields = new[] { "#Settings_Name", "#Settings_TimeLimitInput", "#Settings_DifficultyValue", "#Settings_MaxAttempts" };
        var geometry = await Task.WhenAll(fields.Select(selector => Page.Locator(selector).BoundingBoxAsync()));
        Assert.All(geometry, bounds =>
        {
            Assert.NotNull(bounds);
            Assert.InRange(Math.Abs(bounds.Y - geometry[0]!.Y), 0, 1);
            Assert.InRange(Math.Abs(bounds.Height - geometry[0]!.Height), 0, 1);
        });
        await Page.GetByLabel("Time limit", new() { Exact = true }).FillAsync(seconds.HasValue ? "00:00:15" : "");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save settings", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Settings saved.", new() { Exact = false })).ToBeVisibleAsync();
        Assert.Equal(seconds, await data.Db.Exams.Where(exam => exam.Id == data.Exam.Id).Select(exam => exam.TimeLimitSeconds).SingleAsync());
        Assert.Equal(1, await data.Db.ExamVersions.CountAsync(version => version.ExamId == data.Exam.Id));
        Assert.Equal(ExamPublishStatus.Published, await data.Db.Exams.Where(exam => exam.Id == data.Exam.Id).Select(exam => exam.PublishStatus).SingleAsync());
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/exams/{data.Exam.Id}/start");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#RecordId")).ToHaveAttributeAsync("value", new Regex("^[0-9a-f-]{36}$"));
        await Expect(Page.Locator("[data-exam-version]")).ToHaveTextAsync("v2");
        Assert.Equal(2, await data.Db.ExamVersions.CountAsync(version => version.ExamId == data.Exam.Id));
        var newRecordId = Guid.Parse((await Page.Locator("#RecordId").GetAttributeAsync("value"))!);
        var started = await data.Db.Records.AsNoTracking().SingleAsync(record => record.Id == newRecordId);
        Assert.NotEqual(data.Original.ExamVersionId, started.ExamVersionId);
        if (seconds.HasValue)
            Assert.Equal(TimeSpan.FromSeconds(15), started.EndDateTime - started.StartDateTime);
        else
            Assert.Null(started.EndDateTime);

        // Exercise the public taking surface too, without changing the application's
        // configured global level-test exam or any existing local attempts.
        await using var publicContext = await Browser.NewContextAsync();
        var publicPage = await publicContext.NewPageAsync();
        await publicPage.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/examination/level-test/{newRecordId}");
        if (seconds.HasValue)
            await Expect(publicPage.Locator("#countdown")).ToHaveTextAsync(new Regex(@"^00:00:\d{2}$"));
        else
            await Expect(publicPage.Locator("#countdown")).ToHaveCountAsync(0);
        var old = await data.Db.Records.AsNoTracking().SingleAsync(record => record.Id == data.Original.Id);
        Assert.Equal(data.Original.ExamVersionId, old.ExamVersionId);
        Assert.Equal(data.Original.EndDateTime, old.EndDateTime);
        await publicPage.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/examination/level-test/{old.Id}");
        if (originalSeconds.HasValue)
            await Expect(publicPage.Locator("#countdown")).ToHaveTextAsync(new Regex(@"^00:3[34]:\d{2}$"));
        else
            await Expect(publicPage.Locator("#countdown")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2100)]
    public async Task MyExams_ListAndStartDialogUseSavedTime_AndStudentsCannotManageVersions(int? publishedSeconds)
    {
        await using var teacher = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var learner = await CatalogBrowsingFixture.CreateAsync();
        await using var data = await TimingFixture.CreateAsync(teacher.User.Id, publishedSeconds);
        await data.Db.Exams.Where(exam => exam.Id == data.Exam.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(exam => exam.TimeLimitSeconds, 15));
        data.Db.UserExams.Add(new UserExam { ExamId = data.Exam.Id, UserId = learner.User.Id });
        await data.Db.SaveChangesAsync();
        var publishedDisplay = "00:00:15";

        await LoginAsync(teacher, "/exams");
        var managerRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = data.Exam.Name });
        await Expect(managerRow.Locator("[data-exam-duration]")).ToHaveTextAsync(publishedDisplay);
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Exam library", Exact = true })).ToBeVisibleAsync();
        await managerRow.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = data.Exam.Name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-exam-launch-time]")).ToHaveTextAsync(publishedDisplay);

        await using var learnerContext = await Browser.NewContextAsync();
        var learnerPage = await learnerContext.NewPageAsync();
        await LoginAsync(learner, "/exams", learnerPage);
        var learnerRow = learnerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = data.Exam.Name });
        await Expect(learnerRow.Locator("[data-exam-duration]")).ToHaveTextAsync(publishedDisplay);
        await Expect(learnerPage.GetByRole(AriaRole.Navigation, new() { Name = "Exam library", Exact = true })).ToHaveCountAsync(0);
        await learnerRow.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true }).ClickAsync();
        await Expect(learnerPage.Locator("[data-exam-launch-time]")).ToHaveTextAsync(publishedDisplay);
        await learnerPage.GetByRole(AriaRole.Button, new() { Name = "Start exam", Exact = true }).ClickAsync();
        await Expect(learnerPage.Locator("#RecordId")).ToHaveAttributeAsync("value", new Regex("^[0-9a-f-]{36}$"));
        var id = Guid.Parse((await learnerPage.Locator("#RecordId").GetAttributeAsync("value"))!);
        var attempt = await data.Db.Records.AsNoTracking().SingleAsync(record => record.Id == id);
        Assert.Equal(TimeSpan.FromSeconds(15), attempt.EndDateTime - attempt.StartDateTime);
        await Expect(learnerPage.Locator("#countdown")).ToHaveTextAsync(new Regex(@"^00:00:\d{2}$"));
        foreach (var path in new[] { "/exams?tab=archived", "/exams?tab=versions", $"/exams/version?examId={data.Exam.Id}&versionId={data.Original.ExamVersionId}" })
        {
            var response = await learnerPage.GotoAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
            Assert.True(response?.Status == 403 || learnerPage.Url.Contains("/access-denied", StringComparison.Ordinal), $"Student could access {path}");
        }

    }

    [Fact]
    public async Task Publish_RejectsInvalidQuestionsWithoutChangingHistoricalVersions()
    {
        await using var teacher = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var data = await TimingFixture.CreateAsync(teacher.User.Id);
        await data.Db.Exams.Where(exam => exam.Id == data.Exam.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(exam => exam.PublishStatus, ExamPublishStatus.Draft));
        await data.Db.Questions.Where(question => question.ExamId == data.Exam.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(question => question.Text, ""));
        await LoginAsync(teacher, $"/exams/{data.Exam.Id}/edit?questionId={data.Exam.Questions.Single().Id}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish exam", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Cannot publish yet", Exact = true })).ToBeVisibleAsync();
        Assert.Equal(1, await data.Db.ExamVersions.CountAsync(version => version.ExamId == data.Exam.Id));
        Assert.Equal(ExamPublishStatus.Draft, await data.Db.Exams.Where(exam => exam.Id == data.Exam.Id).Select(exam => exam.PublishStatus).SingleAsync());
    }

    [Fact]
    public async Task CreateExam_AcceptsSecondPrecisionAndDisplaysTheSavedDuration()
    {
        await using var teacher = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var data = new TimingFixture(LocalTestApplication.CreateDbContext());
        var name = $"E2E duration create {Guid.NewGuid():N}";
        try
        {
            await LoginAsync(teacher, "/exams/create");
            await Page.GetByLabel("Exam name", new() { Exact = true }).FillAsync(name);
            await Page.GetByLabel("Level test", new() { Exact = true }).SelectOptionAsync("0");
            await Page.GetByLabel("Time limit", new() { Exact = true }).FillAsync("00:00:15");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Expect(Page.Locator("#ExamId")).ToHaveAttributeAsync("value", new Regex(@"^\d+$"));
            data.Exam = await data.Db.Exams.SingleAsync(exam => exam.Name == name);
            Assert.Equal(15, data.Exam.TimeLimitSeconds);
            await Expect(Page.Locator("[data-exam-header-edit='timeLimit'] [data-exam-header-display]")).ToHaveTextAsync("00:00:15");
        }
        finally
        {
            // Locate the dedicated record even if navigation failed after the POST.
            data.Exam ??= (await data.Db.Exams.SingleOrDefaultAsync(exam => exam.Name == name))!;
        }
    }

    private async Task LoginAsync(CatalogBrowsingFixture user, string path, IPage? targetPage = null)
    {
        var page = targetPage ?? Page;
        await page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await page.GetByLabel("E-mail").FillAsync(user.User.Email!);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(user.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync($"{LocalTestApplication.PortalBaseUrl}{path}");
        var response = await page.GotoAsync(page.Url);
        Assert.True(response?.Ok, $"Authenticated exam page returned HTTP {response?.Status}: {page.Url}");
    }

    private sealed class TimingFixture(ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db => db;
        public Exam Exam { get; set; } = null!;
        public iD_Develops.Models.Record Original { get; private set; } = null!;

        public static async Task<TimingFixture> CreateAsync(string owner, int? publishedSeconds = 2100)
        {
            var fixture = new TimingFixture(LocalTestApplication.CreateDbContext());
            try
            {
                fixture.Exam = new Exam
                {
                    Name = $"E2E timing {Guid.NewGuid():N}",
                    CreatedByUserId = owner,
                    TimeLimitSeconds = publishedSeconds,
                    MaxAttempts = 1,
                    IntroductionPrimaryLanguage = "Answer the question.",
                    Questions = [new OpenQuestion { QuestionNumber = 1, Text = "Where do you live?", CorrectAnswers = [new CorrectAnswer { Text = "Amsterdam" }] }]
                };
                fixture.Db.Exams.Add(fixture.Exam);
                await fixture.Db.SaveChangesAsync();
                fixture.Exam.PublishStatus = ExamPublishStatus.Published;
                await fixture.Db.SaveChangesAsync();
                await new ExamVersionService(fixture.Db).GetOrCreateAttemptVersionAsync(fixture.Exam.Id);
                var version = await fixture.Db.ExamVersions.SingleAsync(version => version.ExamId == fixture.Exam.Id);
                var started = DateTime.UtcNow;
                fixture.Original = new iD_Develops.Models.Record
                {
                    Exam = fixture.Exam,
                    ExamVersion = version,
                    ExamStatus = ExamStatus.InProgress,
                    StartDateTime = started,
                    EndDateTime = publishedSeconds.HasValue ? started.AddSeconds(publishedSeconds.Value) : null,
                    StatusReason = RecordStatusReason.Started,
                    LastActivityUtc = started
                };
                fixture.Db.Records.Add(fixture.Original);
                await fixture.Db.SaveChangesAsync();
                // Use the persisted timestamp as the baseline: PostgreSQL stores microseconds.
                await fixture.Db.Entry(fixture.Original).ReloadAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Exam?.Id is > 0)
                {
                    var ids = await db.Records.Where(record => record.ExamId == Exam.Id).Select(record => record.Id.ToString()).ToListAsync();
                    await db.ParticipantAnswers.Where(answer => answer.Record.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.Records.Where(record => record.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.ApplicationAuditEvents.Where(audit => audit.EntityType == nameof(iD_Develops.Models.Record) && ids.Contains(audit.EntityId!)).ExecuteDeleteAsync();
                    await db.ExamLogs.Where(log => log.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.UserExams.Where(access => access.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.Questions.Where(question => question.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.Exams.Where(exam => exam.Id == Exam.Id).ExecuteDeleteAsync();
                }
            }
            finally
            {
                await db.DisposeAsync();
            }
        }
    }
}
