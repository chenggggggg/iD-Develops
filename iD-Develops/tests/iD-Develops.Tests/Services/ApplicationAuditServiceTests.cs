using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace iD_Develops.Tests.Services;

public sealed class ApplicationAuditServiceTests
{
    [Fact]
    public async Task SaveChanges_CapturesExamAttemptLifecycleInSameContext()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var user = new ApplicationUser
        {
            Id = "student-1",
            UserName = "student@example.test",
            NormalizedUserName = "STUDENT@EXAMPLE.TEST",
            Email = "student@example.test",
            NormalizedEmail = "STUDENT@EXAMPLE.TEST"
        };
        var exam = new Exam
        {
            Name = "Audit test exam",
            CreatedByUserId = user.Id,
            Difficulty = DifficultyLevel.A1,
            IntroductionPrimaryLanguage = "Start",
            ResultGradeDisplayMode = ResultGradeDisplayMode.Score
        };

        dbContext.Users.Add(user);
        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var record = new iD_Develops.Models.Record
        {
            UserId = user.Id,
            ExamId = exam.Id,
            StartDateTime = DateTime.UtcNow,
            ExamStatus = ExamStatus.InProgress,
            StatusReason = RecordStatusReason.Started
        };
        dbContext.Records.Add(record);
        await dbContext.SaveChangesAsync();

        record.ExamStatus = ExamStatus.Completed;
        record.StatusReason = RecordStatusReason.ManualSubmit;
        await dbContext.SaveChangesAsync();

        var auditEvents = await dbContext.ApplicationAuditEvents
            .OrderBy(auditEvent => auditEvent.Id)
            .ToListAsync();

        Assert.Collection(
            auditEvents,
            started =>
            {
                Assert.Equal(ApplicationAuditEventTypes.ExamAttemptStarted, started.EventType);
                Assert.Equal(record.Id.ToString(), started.EntityId);
                Assert.Equal(user.Id, started.SubjectUserId);
            },
            completed =>
            {
                Assert.Equal(ApplicationAuditEventTypes.ExamAttemptStatusChanged, completed.EventType);
                Assert.Equal(AuditEventOutcome.Success, completed.Outcome);
                Assert.Contains("Completed", completed.Summary, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task RecordAsync_DeduplicatesExternalEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new ApplicationAuditService(
            dbContext,
            new HttpContextAccessor(),
            NullLogger<ApplicationAuditService>.Instance);
        var entry = new ApplicationAuditEntry(
            ApplicationAuditEventTypes.PurchaseCompleted,
            AuditEventCategory.Commerce,
            AuditEventOutcome.Success,
            "Purchase completed.",
            DeduplicationKey: "stripe:event-1:purchase-completed");

        Assert.True(await service.RecordAsync(entry));
        Assert.True(await service.RecordAsync(entry));
        Assert.Equal(1, await dbContext.ApplicationAuditEvents.CountAsync());
    }
}
