using System.Data.Common;
using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace iD_Develops.Tests.Services;

public sealed class RecordLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubmitExamAsync_DeadlinePassed_CompletesWithUnansweredQuestions(bool savedAnswer)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = await SeedAsync(db);
        db.Questions.Add(new OpenQuestion { ExamId = record.ExamId, Text = "Unanswered question", QuestionNumber = 2, Score = 3 });
        await db.SaveChangesAsync();
        if (!savedAnswer)
            await db.ParticipantAnswers.ExecuteDeleteAsync();
        var options = Options.Create(new RecordLifecycleOptions());
        var versions = new ExamVersionService(db);
        var answers = new ParticipantAnswerService(db, options);
        var records = CreateService(db);
        var flow = new ExamTakeFlowService(
            new ExamAttemptStateService(records, new QuestionService(db), versions, answers), records, answers, versions);
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["recordId"] = record.Id.ToString(),
            ["examId"] = record.ExamId.ToString(),
            ["questionId"] = record.Exam.Questions.First().Id.ToString(),
            ["answer"] = "Late answer"
        });

        // The timer's final answer save may arrive after expiry. It must still complete
        // using only the previously saved answers, including when none were answered.
        Assert.Equal(StatusCodes.Status409Conflict, (await flow.SaveAnswerAsync(form)).StatusCode);
        Assert.True((await flow.SubmitExamAsync(form, _ => "/completed")).Success);
        Assert.True((await flow.SubmitExamAsync(form, _ => "/completed")).Success);

        var completed = await db.Records.AsNoTracking().SingleAsync();
        Assert.Equal(ExamStatus.Completed, completed.ExamStatus);
        Assert.Equal(RecordStatusReason.DeadlinePassed, completed.StatusReason);
        Assert.Equal(savedAnswer ? 2d : 0d, completed.Score);
        Assert.Equal(savedAnswer ? 1 : 0, await db.ParticipantAnswers.CountAsync());
        var audit = Assert.Single(await db.ApplicationAuditEvents.Where(item =>
            item.EventType == ApplicationAuditEventTypes.ExamAttemptStatusChanged).ToListAsync());
        Assert.Contains("DeadlinePassed", audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetExamStatusAsync_StaleTrackedAttempt_ExpiresAndAuditsOnlyOnce()
    {
        using var factory = new SqliteTestDbFactory();
        await using var first = factory.CreateDbContext();
        var record = await SeedAsync(first);
        await using var second = factory.CreateDbContext();
        await second.Records.SingleAsync();

        Assert.Equal(ExamStatus.Completed, await CreateService(first).GetExamStatusAsync(record.Id));
        Assert.Equal(ExamStatus.Completed, await CreateService(second).GetExamStatusAsync(record.Id));

        await AssertExpiredOnceAsync(second, record.Id);
    }

    [Fact]
    public async Task FinalizeExpiredRecordsAsync_RepeatedCleanup_PreservesScoreAndSingleAudit()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = await SeedAsync(db);
        var service = CreateService(db);

        Assert.Equal(1, await service.FinalizeExpiredRecordsAsync());
        Assert.Equal(0, await service.FinalizeExpiredRecordsAsync());
        Assert.Equal(ExamStatus.Completed, await service.GetExamStatusAsync(record.Id));

        await AssertExpiredOnceAsync(db, record.Id);
    }

    [Fact]
    public async Task GetExamStatusAsync_AnotherRequestCompletedAttempt_DoesNotExpireIt()
    {
        using var factory = new SqliteTestDbFactory();
        await using var first = factory.CreateDbContext();
        var record = await SeedAsync(first);
        await using var stale = factory.CreateDbContext();
        await stale.Records.SingleAsync();
        await CreateService(first).CompleteRecordAsync(record.Id, record.EndDateTime!.Value.AddSeconds(-1));

        Assert.Equal(ExamStatus.Completed, await CreateService(stale).GetExamStatusAsync(record.Id));

        var audit = Assert.Single(await stale.ApplicationAuditEvents
            .Where(item => item.EventType == ApplicationAuditEventTypes.ExamAttemptStatusChanged).ToListAsync());
        Assert.Contains("Completed", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteRecordAsync_TwoLateSubmissions_FinalizesAndAuditsOnlyOnce()
    {
        using var factory = new SqliteTestDbFactory();
        await using var first = factory.CreateDbContext();
        var record = await SeedAsync(first);
        await using var stale = factory.CreateDbContext();
        await stale.Records.SingleAsync();

        await CreateService(first).CompleteRecordAsync(record.Id, DateTime.UtcNow);
        await CreateService(stale).CompleteRecordAsync(record.Id, DateTime.UtcNow);

        await AssertExpiredOnceAsync(stale, record.Id);
    }

    [Fact]
    public async Task GetExamStatusAsync_DeadlineWasExtended_DoesNotExpireStaleSnapshot()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = await SeedAsync(db);
        await using var stale = factory.CreateDbContext();
        await stale.Records.SingleAsync();
        await db.Records.Where(item => item.Id == record.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.EndDateTime, DateTime.UtcNow.AddHours(1)));

        Assert.Equal(ExamStatus.InProgress, await CreateService(stale).GetExamStatusAsync(record.Id));
        Assert.Empty(await stale.ApplicationAuditEvents
            .Where(item => item.EventType == ApplicationAuditEventTypes.ExamAttemptStatusChanged).ToListAsync());
    }

    [Fact]
    public async Task GetExamStatusAsync_AbandonedUntimedAttempt_CancelsAndAuditsOnlyOnce()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = await SeedAsync(db, untimed: true);
        await using var stale = factory.CreateDbContext();
        await stale.Records.SingleAsync();

        Assert.Equal(ExamStatus.Cancelled, await CreateService(db).GetExamStatusAsync(record.Id));
        Assert.Equal(ExamStatus.Cancelled, await CreateService(stale).GetExamStatusAsync(record.Id));

        Assert.Single(await stale.ApplicationAuditEvents
            .Where(item => item.EventType == ApplicationAuditEventTypes.ExamAttemptStatusChanged).ToListAsync());
    }

    [Fact]
    public async Task StartAttemptAtomicAsync_ExpiresPreviousAttemptInsideExistingTransaction()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = await SeedAsync(db);
        var user = new ApplicationUser { UserName = "lifecycle@example.test" };
        db.Users.Add(user);
        record.UserId = user.Id;
        await db.SaveChangesAsync();

        var started = await CreateService(db).StartAttemptAtomicAsync(
            user.Id, record.ExamId, null, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));

        Assert.NotEqual(Guid.Empty, started);
        Assert.NotEqual(record.Id, started);
        await AssertExpiredOnceAsync(db, record.Id);
    }

    [Fact]
    public async Task GetExamStatusAsync_TransientCommitFailure_RetriesWithoutDuplicateAudit()
    {
        using var factory = new SqliteTestDbFactory();
        var failure = new CommitFailureInterceptor();
        await using var db = factory.CreateDbContext(options => options
            .ReplaceService<IExecutionStrategyFactory, RetryStrategyFactory>().AddInterceptors(failure));
        var record = await SeedAsync(db);
        failure.FailNextCommit = true;

        Assert.Equal(ExamStatus.Completed, await CreateService(db).GetExamStatusAsync(record.Id));

        Assert.False(failure.FailNextCommit);
        await AssertExpiredOnceAsync(db, record.Id);
    }

    [Fact]
    public async Task StartAttemptAtomicAsync_ContinuesEarlierVersion_AndCountsAttemptsAcrossVersions()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = await SeedAsync(db);
        var user = new ApplicationUser { UserName = "versions@example.test" };
        db.Users.Add(user);
        record.UserId = user.Id;
        record.StartDateTime = DateTime.UtcNow;
        record.EndDateTime = record.StartDateTime.AddHours(1);
        record.Exam.PublishStatus = ExamPublishStatus.Published;
        record.Exam.TimeLimitSeconds = 3600;
        await db.SaveChangesAsync();
        var versions = new ExamVersionService(db);
        record.ExamVersionId = (await versions.GetOrCreateAttemptVersionAsync(record.ExamId))!.ExamVersionId;
        await db.SaveChangesAsync();
        var firstVersionId = record.ExamVersionId;
        record.Exam.TimeLimitSeconds = 2400;
        await db.SaveChangesAsync();
        var newVersion = (await versions.GetOrCreateAttemptVersionAsync(record.ExamId))!;
        var records = CreateService(db);

        var continued = await records.StartAttemptAtomicAsync(user.Id, record.ExamId, newVersion.ExamVersionId,
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(40));

        Assert.Equal(record.Id, continued);
        Assert.Single(await db.Records.ToListAsync());
        Assert.Equal(firstVersionId, record.ExamVersionId);
        Assert.Equal(TimeSpan.FromHours(1), record.EndDateTime - record.StartDateTime);
        Assert.Equal(1, await records.CountAttemptsAsync(user.Id, record.ExamId));
        Assert.Equal(0, await records.CountAttemptsAsync(user.Id, record.ExamId, newVersion.ExamVersionId));
        Assert.Single(await records.GetInProgressRecordsAsync(user.Id, record.ExamId));
    }

    private static RecordService CreateService(ApplicationDbContext db)
    {
        var options = Options.Create(new RecordLifecycleOptions { NoTimeLimitInactivityDays = 14 });
        return new RecordService(db, NullLogger<RecordService>.Instance, options,
            new ExamVersionService(db), new ParticipantAnswerService(db, options), new ExamEvaluationService());
    }

    private static async Task<iD_Develops.Models.Record> SeedAsync(ApplicationDbContext db, bool untimed = false)
    {
        var now = DateTime.UtcNow;
        var question = new OpenQuestion
        {
            Text = "Where do you live?",
            QuestionNumber = 1,
            Score = 2,
            CorrectAnswers = [new CorrectAnswer { Text = "Amsterdam" }]
        };
        var record = new iD_Develops.Models.Record
        {
            Exam = new Exam
            {
                Name = "Lifecycle test",
                CreatedByUserId = "teacher",
                IntroductionPrimaryLanguage = "Start",
                Questions = [question]
            },
            ExamStatus = ExamStatus.InProgress,
            StartDateTime = untimed ? now.AddDays(-40) : now.AddMinutes(-10),
            EndDateTime = untimed ? null : now.AddMinutes(-1),
            LastActivityUtc = untimed ? now.AddDays(-40) : now.AddMinutes(-5)
        };
        db.Records.Add(record);
        db.ParticipantAnswers.Add(new ParticipantAnswer
        {
            Record = record,
            Question = question,
            AnswerText = "Amsterdam",
            Timestamp = record.StartDateTime.AddMinutes(1)
        });
        await db.SaveChangesAsync();
        return record;
    }

    private static async Task AssertExpiredOnceAsync(ApplicationDbContext db, Guid recordId)
    {
        var record = await db.Records.AsNoTracking().SingleAsync(item => item.Id == recordId);
        Assert.Equal(ExamStatus.Completed, record.ExamStatus);
        Assert.Equal(2d, record.Score);
        Assert.Single(await db.ApplicationAuditEvents.Where(item =>
            item.EntityId == recordId.ToString() && item.EventType == ApplicationAuditEventTypes.ExamAttemptStatusChanged).ToListAsync());
    }

    public sealed class RetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create()
            => new NpgsqlRetryingExecutionStrategy(dependencies, 2, TimeSpan.Zero, null);
    }

    private sealed class CommitFailureInterceptor : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new PostgresException("Simulated serialization failure", "ERROR", "ERROR", "40001");
            }
            return ValueTask.FromResult(result);
        }
    }
}
