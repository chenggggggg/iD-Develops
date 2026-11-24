using System.Globalization;
using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Pages;

public sealed class PublicLevelTestSelectionTests
{
    [Theory]
    [InlineData("a1", "en-US", "English a1", 90)]
    [InlineData("a1", "nl-NL", "Nederlands a1", 90)]
    [InlineData("a2", "en-US", "English a2", 240)]
    [InlineData("a2", "nl-NL", "Nederlands a2", 240)]
    public async Task SelectionReadsCorrectIntroductionAndStartsOnlyTheSelectedExam(string level, string culture, string introduction, int seconds)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exams = await SeedAsync(db);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var page = CreatePage(db);
            Assert.IsType<PageResult>(await page.OnGetAsync());
            Assert.Equal(2, page.Tests.Count);
            Assert.All(page.Tests, option => Assert.True(option.IsAvailable));
            Assert.IsType<PageResult>(await page.OnGetAsync(level));
            Assert.Equal(introduction, page.IntroductionHtml);
            Assert.Equal(seconds, page.SelectedTest!.Descriptor!.DurationSeconds);
            Assert.Empty(await db.Records.ToListAsync());
            Assert.Empty(await db.ExamVersions.ToListAsync());
            Assert.IsType<RedirectToPageResult>(await page.OnPostCreateRecordAsync(level));
            var record = await db.Records.Include(item => item.ExamVersion).SingleAsync();
            Assert.Equal(exams.Single(exam => exam.PublicSlug == PublicLevelTests.SlugForLevel(level)).Id, record.ExamId);
            var preserved = await new ExamVersionService(db).GetPublishedDescriptorAsync(record.ExamId, record.ExamVersionId);
            Assert.Equal(PublicLevelTests.SlugForLevel(level), preserved!.PublicSlug);
            Assert.Equal(TimeSpan.FromSeconds(seconds), record.EndDateTime - record.StartDateTime);
            Assert.Null(record.UserId);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("a2")]
    [InlineData("private-exam")]
    [InlineData("123")]
    public async Task UnavailableOrForgedSelectionCannotStartAnotherExam(string level)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exams = await SeedAsync(db);
        exams[1].PublishStatus = ExamPublishStatus.Draft;
        await db.SaveChangesAsync();
        var page = CreatePage(db);
        await page.OnGetAsync();
        Assert.True(page.Tests[0].IsAvailable);
        Assert.False(page.Tests[1].IsAvailable);
        Assert.IsType<NotFoundResult>(await page.OnPostCreateRecordAsync(level));
        if (level != "a2") Assert.IsType<NotFoundResult>(await page.OnGetAsync(level));
        Assert.Empty(await db.Records.ToListAsync());
    }

    [Fact]
    public async Task AdminCanAssignEachLevelOnceWithoutChangingOtherSettingsOnConflict()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exams = await SeedAsync(db);
        exams[1].PublicSlug = null;
        var candidate = new Exam { Name = "Unchanged", IntroductionPrimaryLanguage = "Intro", CreatedByUserId = "owner" };
        db.Exams.Add(candidate);
        await db.SaveChangesAsync();
        var service = new ExamEditMutationService(new ExamService(db), new QuestionService(db), new ExamVersionService(db), null!);
        var settings = new CreateExamInputModel { Name = "A2", IsPublicLevelTest = true, PublicLevelTestLevel = "a2" };
        Assert.True((await service.SaveSettingsAsync(exams[1].Id, settings, "owner", true)).Success);
        Assert.Equal(PublicLevelTests.A2Slug, exams[1].PublicSlug);
        foreach (var level in new[] { "a1", "a2", "private-exam" })
        {
            settings.PublicLevelTestLevel = level;
            var result = await service.SaveSettingsAsync(candidate.Id, settings, "owner", true);
            Assert.False(result.Success);
            Assert.Equal(level == "private-exam" ? 400 : 409, result.StatusCode);
            Assert.Equal("Unchanged", candidate.Name);
            Assert.Null(candidate.PublicSlug);
        }
    }

    private static LevelTestIntroductionModel CreatePage(ApplicationDbContext db)
    {
        var versions = new ExamVersionService(db);
        return new(new ExamService(db), versions, new RecordService(db, NullLogger<RecordService>.Instance,
            Options.Create(new RecordLifecycleOptions()), versions, null!, null!), new Localizer());
    }

    private static async Task<List<Exam>> SeedAsync(ApplicationDbContext db)
    {
        var exams = new[] { "a1", "a2" }.Select(level => new Exam
        {
            Name = level.ToUpperInvariant(),
            CreatedByUserId = "owner",
            PublicSlug = PublicLevelTests.SlugForLevel(level),
            PublishStatus = ExamPublishStatus.Published,
            TimeLimitSeconds = level == "a1" ? 90 : 240,
            IntroductionPrimaryLanguage = $"English {level}",
            IntroductionSecondaryLanguage = $"Nederlands {level}",
            Questions = [new OpenQuestion { QuestionNumber = 1, Text = "Where?", Score = 1, CorrectAnswers = [new() { Text = "Amsterdam" }] }]
        }).ToList();
        db.Exams.AddRange(exams);
        await db.SaveChangesAsync();
        return exams;
    }

    private sealed class Localizer : IStringLocalizer<LevelTestIntroductionModel>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
