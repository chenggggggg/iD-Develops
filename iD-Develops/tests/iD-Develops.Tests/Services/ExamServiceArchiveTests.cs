using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class ExamServiceArchiveTests
{
    [Fact]
    public async Task ArchiveExam_PreservesExamAndClearsPublicRoute()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var exam = new Exam
        {
            Name = "Dutch level test",
            CreatedByUserId = "owner",
            Difficulty = DifficultyLevel.A1,
            IntroductionPrimaryLanguage = string.Empty,
            PublishStatus = ExamPublishStatus.Published,
            PublicSlug = "level-test"
        };
        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var service = new ExamService(dbContext);
        var result = await service.ArchiveExamAsync(exam.Id);

        Assert.True(result.Success);
        dbContext.ChangeTracker.Clear();
        var archived = await dbContext.Exams.SingleAsync(e => e.Id == exam.Id);
        Assert.Equal(ExamPublishStatus.Archived, archived.PublishStatus);
        Assert.False(archived.IsDeleted);
        Assert.Null(archived.PublicSlug);
    }

    [Fact]
    public async Task DeleteExam_WithHistoricalRecord_IsRejected()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var exam = new Exam
        {
            Name = "Completed exam",
            CreatedByUserId = "owner",
            Difficulty = DifficultyLevel.A1,
            IntroductionPrimaryLanguage = string.Empty,
            PublishStatus = ExamPublishStatus.Published
        };
        exam.Records.Add(new iD_Develops.Models.Record
        {
            StartDateTime = DateTime.UtcNow,
            ExamStatus = ExamStatus.Completed,
            ParticipantAnswers = new List<ParticipantAnswer>()
        });
        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var service = new ExamService(dbContext);
        var result = await service.DeleteExamByIdAsync(exam.Id);

        Assert.False(result.Success);
        Assert.Contains("Archive", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False((await dbContext.Exams.SingleAsync(e => e.Id == exam.Id)).IsDeleted);
    }
}
