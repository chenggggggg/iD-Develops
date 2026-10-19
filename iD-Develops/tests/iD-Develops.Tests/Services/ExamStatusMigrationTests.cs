using iD_Develops.Data.Migrations;
using iD_Develops.Models;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace iD_Develops.Tests.Services;

public sealed class ExamStatusMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_CompletesLegacyExpiredAttempts_PreservesAnswersAndHistory(bool deleted)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var question = new OpenQuestion { Text = "Question", QuestionNumber = 1 };
        var record = new iD_Develops.Models.Record
        {
            Exam = new Exam { Name = "Legacy exam", CreatedByUserId = "teacher", IntroductionPrimaryLanguage = "Start", Questions = [question] },
            ExamStatus = ExamStatus.InProgress,
            StartDateTime = DateTime.UtcNow.AddHours(-2),
            EndDateTime = DateTime.UtcNow.AddHours(-1),
            StatusChangedAtUtc = DateTime.UtcNow.AddMinutes(-59),
            Score = 5,
            IsDeleted = deleted
        };
        db.Records.Add(record);
        db.ParticipantAnswers.Add(new ParticipantAnswer
        {
            Record = record,
            Question = question,
            AnswerText = "Saved answer",
            Timestamp = record.StartDateTime
        });
        await db.SaveChangesAsync();
        // Reproduce the persisted text without reintroducing the removed enum member.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Records\" SET \"ExamStatus\" = 'Overdue' WHERE \"Id\" = {record.Id}");
        var auditCount = await db.ApplicationAuditEvents.CountAsync();
        db.ChangeTracker.Clear();
        var migration = new CompleteExpiredExamAttempts();
        Assert.Contains("20261019080000_CompleteExpiredExamAttempts", db.Database.GetMigrations());

        foreach (var operation in migration.UpOperations.Cast<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(operation.Sql);

        var migrated = await db.Records.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(ExamStatus.Completed, migrated.ExamStatus);
        Assert.Equal(RecordStatusReason.DeadlinePassed, migrated.StatusReason);
        Assert.Equal(record.Score, migrated.Score);
        Assert.Equal(record.StatusChangedAtUtc, migrated.StatusChangedAtUtc);
        Assert.Equal(deleted, migrated.IsDeleted);
        Assert.Equal("Saved answer", (await db.ParticipantAnswers.SingleAsync()).AnswerText);
        Assert.Equal(auditCount, await db.ApplicationAuditEvents.CountAsync());
        Assert.Empty(migration.DownOperations);
    }

    [Theory]
    [InlineData(ExamStatus.NotStarted)]
    [InlineData(ExamStatus.InProgress)]
    [InlineData(ExamStatus.Completed)]
    [InlineData(ExamStatus.Cancelled)]
    [InlineData(ExamStatus.Invalid)]
    [InlineData(ExamStatus.Disconnected)]
    public async Task Migration_PreservesOtherStatusesAndReasons(ExamStatus status)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        db.Records.Add(new iD_Develops.Models.Record
        {
            Exam = new Exam { Name = "Other exam", CreatedByUserId = "teacher", IntroductionPrimaryLanguage = "Start" },
            ExamStatus = status,
            StatusReason = RecordStatusReason.ManualSubmit,
            StartDateTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        foreach (var operation in new CompleteExpiredExamAttempts().UpOperations.Cast<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(operation.Sql);

        var record = await db.Records.SingleAsync();
        Assert.Equal(status, record.ExamStatus);
        Assert.Equal(RecordStatusReason.ManualSubmit, record.StatusReason);
    }
}
