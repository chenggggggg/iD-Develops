using iD_Develops.Configuration;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace iD_Develops.Tests.Pages;

public sealed class LevelTestTimingTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(null)]
    public async Task PublicStart_UsesSavedDurationWithoutRepublishing_AndPreservesExistingDeadlines(int? updatedDuration)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = new Exam
        {
            Name = "Public level test",
            CreatedByUserId = "owner",
            PublicSlug = "level-test",
            TimeLimitSeconds = 35 * 60,
            IntroductionPrimaryLanguage = "Answer the question.",
            MaxAttempts = -1,
            Questions = [new OpenQuestion { QuestionNumber = 1, Text = "Where do you live?", CorrectAnswers = [new CorrectAnswer { Text = "Amsterdam" }] }]
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var versions = new ExamVersionService(db);
        exam.PublishStatus = ExamPublishStatus.Published;
        await db.SaveChangesAsync();
        await versions.GetOrCreateAttemptVersionAsync(exam.Id);
        var records = new RecordService(db, NullLogger<RecordService>.Instance,
            Options.Create(new RecordLifecycleOptions()), versions, null!, null!);
        var exams = new ExamService(db);
        var page = new LevelTestIntroductionModel(exams, versions, records, new TestLocalizer());
        Assert.IsType<RedirectToPageResult>(await page.OnPostCreateRecordAsync());
        var original = await db.Records.AsNoTracking().SingleAsync();

        var mutation = new ExamEditMutationService(exams, new QuestionService(db), versions, null!);
        var saved = await mutation.SaveSettingsAsync(exam.Id, new CreateExamInputModel
        {
            Name = exam.Name,
            TimeLimitSeconds = updatedDuration,
            DifficultyValue = 0,
            IsPublicLevelTest = true,
            IntroductionPrimaryLanguage = exam.IntroductionPrimaryLanguage
        }, "owner", isAdmin: true);
        Assert.True(saved.Success);

        await page.OnGetAsync();
        Assert.True(page.IsLevelTestAvailable);
        Assert.Equal(updatedDuration.HasValue, page.HasTimeLimit);
        Assert.Equal(updatedDuration.HasValue ? "00:00:15" : "No time limit", page.DurationDisplay);
        Assert.IsType<RedirectToPageResult>(await page.OnPostCreateRecordAsync());

        var attempts = await db.Records.AsNoTracking().ToListAsync();
        var latest = Assert.Single(attempts, record => record.ExamVersionId != original.ExamVersionId);
        Assert.Null(latest.UserId);
        if (updatedDuration.HasValue)
            Assert.Equal(TimeSpan.FromSeconds(15), latest.EndDateTime - latest.StartDateTime);
        else
            Assert.Null(latest.EndDateTime);
        var persisted = attempts.Single(record => record.Id == original.Id);
        Assert.Equal(original.ExamVersionId, persisted.ExamVersionId);
        Assert.Equal(original.EndDateTime, persisted.EndDateTime);
        Assert.Equal(ExamPublishStatus.Published, (await db.Exams.AsNoTracking().SingleAsync()).PublishStatus);

    }

    [Fact]
    public async Task Settings_UsesSavedDurationWithoutTreatingInvalidInputAsSaved()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = new Exam
        {
            Name = "Published exam",
            CreatedByUserId = "owner",
            TimeLimitSeconds = 35 * 60,
            IntroductionPrimaryLanguage = "Intro"
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var versions = new ExamVersionService(db);
        exam.PublishStatus = ExamPublishStatus.Published;
        await db.SaveChangesAsync();
        await versions.GetOrCreateAttemptVersionAsync(exam.Id);
        var exams = new ExamService(db);
        var questions = new QuestionService(db);
        var model = new SettingsModel(new ExamEditFlowService(exams, questions, new ConfigurationBuilder().Build()),
            new ExamEditMutationService(exams, questions, versions, null!), exams, questions)
        {
            ExamId = exam.Id,
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test"))
                }
            }
        };
        Assert.IsType<PageResult>(await model.OnGetAsync());
        Assert.Equal("00:35:00", model.Settings.TimeLimitInput);

        model.Settings.TimeLimitInput = "not-a-duration";
        Assert.IsType<PageResult>(await model.OnPostAsync());
        Assert.False(model.ModelState.IsValid);
        Assert.Equal(2100, (await db.Exams.AsNoTracking().SingleAsync()).TimeLimitSeconds);
        Assert.Equal("not-a-duration", model.Settings.TimeLimitInput);

        exam.TimeLimitSeconds = 15;
        await db.SaveChangesAsync();
        Assert.IsType<PageResult>(await model.OnGetAsync());
        Assert.Equal("00:00:15", model.Settings.TimeLimitInput);
        Assert.Single(await db.ExamVersions.ToListAsync());
    }

    [Fact]
    public async Task UnavailableLevelTest_DoesNotAdvertiseAFabricatedDurationOrStartAnAttempt()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var page = new LevelTestIntroductionModel(new ExamService(db), new ExamVersionService(db), null!, new TestLocalizer());
        await page.OnGetAsync();
        Assert.False(page.IsLevelTestAvailable);
        Assert.False(page.HasTimeLimit);
        Assert.Equal("—", page.DurationDisplay);
        Assert.IsType<NotFoundResult>(await page.OnPostCreateRecordAsync());
        Assert.Empty(await db.Records.ToListAsync());
    }

    private sealed class TestLocalizer : IStringLocalizer<LevelTestIntroductionModel>
    {
        public LocalizedString this[string name] => new(name, name == "NoTimeLimit" ? "No time limit" : name);
        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
