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

public sealed class ParticipantAnswerServiceTests
{
    [Fact]
    public async Task LevelTest_SaveValidateAndSubmit_PersistsAnswerAndCompletedScore()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = CreateRetryingContext(factory);
        var (record, question) = await SeedAttemptAsync(db);
        question.CorrectAnswers.Add(new CorrectAnswer { Text = "Amsterdam" });
        await db.SaveChangesAsync();
        var versions = new ExamVersionService(db);
        record.Exam.PublishStatus = ExamPublishStatus.Published;
        await db.SaveChangesAsync();
        Assert.NotNull(await versions.GetOrCreateAttemptVersionAsync(record.ExamId));
        record.ExamVersionId = (await db.ExamVersions.SingleAsync()).Id;
        await db.SaveChangesAsync();

        var answers = CreateService(db);
        var records = new RecordService(db, NullLogger<RecordService>.Instance,
            Options.Create(new RecordLifecycleOptions()), versions, answers, new ExamEvaluationService());
        var flow = new ExamTakeFlowService(
            new ExamAttemptStateService(records, new QuestionService(db), versions, answers),
            records, answers, versions);
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["examId"] = record.ExamId.ToString(),
            ["recordId"] = record.Id.ToString(),
            ["questionId"] = question.Id.ToString(),
            ["answer"] = "Amsterdam"
        });

        var saved = await flow.SaveAnswerAsync(form);
        Assert.True(saved.Success, saved.ErrorMessage);
        Assert.Equal(StatusCodes.Status200OK, saved.StatusCode);
        var validation = await flow.ValidateMissingQuestionsAsync(record.ExamId, record.Id);
        Assert.True(validation.Success, validation.ErrorMessage);
        Assert.Empty(validation.MissingQuestionNumbers);

        var completedUrl = $"/en-us/examination/completed/{record.Id}";
        var result = await flow.SubmitExamAsync(form, _ => completedUrl);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(completedUrl, result.RedirectUrl);
        Assert.True((await flow.SubmitExamAsync(form, _ => completedUrl)).Success);

        db.ChangeTracker.Clear();
        var completed = await db.Records.SingleAsync();
        Assert.Equal(ExamStatus.Completed, completed.ExamStatus);
        Assert.Equal(1d, completed.Score);
        Assert.Null(completed.UserId);
        Assert.Equal("Amsterdam", (await db.ParticipantAnswers.SingleAsync()).AnswerText);
    }

    [Fact]
    public async Task SaveParticipantAnswerAsync_WithPostgresRetryStrategy_PersistsAnswerAndActivity()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = CreateRetryingContext(factory);
        var (record, question) = await SeedAttemptAsync(db);
        var answer = NewAnswer(record, question, "  Amsterdam  ");

        Assert.True(await CreateService(db).SaveParticipantAnswerAsync(answer));

        db.ChangeTracker.Clear();
        var saved = await db.ParticipantAnswers.SingleAsync();
        Assert.Equal("Amsterdam", saved.AnswerText);
        Assert.False(saved.IsDeleted);
        Assert.Equal(answer.Timestamp, (await db.Records.SingleAsync()).LastActivityUtc);
    }

    [Theory]
    [InlineData(null, "Amsterdam")]
    [InlineData("Rotterdam", "Amsterdam")]
    [InlineData("Rotterdam", "  ")]
    public async Task SaveParticipantAnswerAsync_TransientCommitFailure_RetriesEntireSave(
        string? previousAnswer, string submittedAnswer)
    {
        using var factory = new SqliteTestDbFactory();
        var failure = new SerializationFailureInterceptor();
        await using var db = CreateRetryingContext(factory, failure);
        var (record, question) = await SeedAttemptAsync(db);
        if (previousAnswer != null)
        {
            db.ParticipantAnswers.Add(new ParticipantAnswer
            {
                RecordId = record.Id,
                QuestionId = question.Id,
                AnswerText = previousAnswer,
                Timestamp = record.StartDateTime
            });
            await db.SaveChangesAsync();
        }
        failure.FailNextCommit = true;
        var initialCommitAttempts = failure.CommitAttempts;
        var answer = NewAnswer(record, question, submittedAnswer);

        Assert.True(await CreateService(db).SaveParticipantAnswerAsync(answer));

        db.ChangeTracker.Clear();
        Assert.Equal(initialCommitAttempts + 2, failure.CommitAttempts);
        var saved = await db.ParticipantAnswers.SingleAsync();
        Assert.Equal(submittedAnswer.Trim(), saved.AnswerText);
        Assert.Equal(string.IsNullOrWhiteSpace(submittedAnswer), saved.IsDeleted);
        Assert.Equal(answer.Timestamp, saved.Timestamp);
        Assert.Equal(answer.Timestamp, (await db.Records.SingleAsync()).LastActivityUtc);
    }

    [Theory]
    [InlineData(ExamStatus.Completed, false)]
    [InlineData(ExamStatus.Cancelled, false)]
    [InlineData(ExamStatus.InProgress, true)]
    public async Task SaveParticipantAnswerAsync_ClosedOrExpiredAttempt_RejectsAnswer(
        ExamStatus status, bool expired)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = CreateRetryingContext(factory);
        var (record, question) = await SeedAttemptAsync(db);
        record.ExamStatus = status;
        if (expired)
            record.EndDateTime = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.False(await CreateService(db).SaveParticipantAnswerAsync(NewAnswer(record, question, "Late answer")));

        Assert.Empty(await db.ParticipantAnswers.ToListAsync());
    }

    [Fact]
    public async Task SaveParticipantAnswerAsync_OlderAnswer_DoesNotOverwriteLatestAnswer()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = CreateRetryingContext(factory);
        var (record, question) = await SeedAttemptAsync(db);
        var latest = NewAnswer(record, question, "Latest answer");
        var service = CreateService(db);
        Assert.True(await service.SaveParticipantAnswerAsync(latest));
        var stale = NewAnswer(record, question, "Stale answer");
        stale.Timestamp = latest.Timestamp.AddSeconds(-1);

        Assert.False(await service.SaveParticipantAnswerAsync(stale));
        db.ChangeTracker.Clear();
        Assert.Equal("Latest answer", (await db.ParticipantAnswers.SingleAsync()).AnswerText);
    }

    private static ApplicationDbContext CreateRetryingContext(
        SqliteTestDbFactory factory, DbTransactionInterceptor? interceptor = null)
        => factory.CreateDbContext(options =>
        {
            // Exercise the production provider's transaction/retry guard against an isolated database.
            options.ReplaceService<IExecutionStrategyFactory, PostgresRetryStrategyFactory>();
            if (interceptor != null)
                options.AddInterceptors(interceptor);
        });

    private static ParticipantAnswerService CreateService(ApplicationDbContext db)
        => new(db, Options.Create(new RecordLifecycleOptions { SubmissionGraceSeconds = 30 }));

    private static async Task<(iD_Develops.Models.Record Record, Question Question)> SeedAttemptAsync(ApplicationDbContext db)
    {
        var question = new OpenQuestion { Text = "Where do you live?", QuestionNumber = 1, Score = 1 };
        var exam = new Exam
        {
            Name = "Level test",
            CreatedByUserId = "teacher",
            IntroductionPrimaryLanguage = "Introduction",
            PublicSlug = "level-test",
            PublishStatus = ExamPublishStatus.Published,
            Questions = [question]
        };
        var now = DateTime.UtcNow;
        var record = new iD_Develops.Models.Record
        {
            Exam = exam,
            ExamStatus = ExamStatus.InProgress,
            StartDateTime = now.AddMinutes(-1),
            EndDateTime = now.AddMinutes(30),
            LastActivityUtc = now.AddMinutes(-1)
        };
        db.Records.Add(record);
        await db.SaveChangesAsync();
        return (record, question);
    }

    private static ParticipantAnswer NewAnswer(iD_Develops.Models.Record record, Question question, string text)
        => new()
        {
            RecordId = record.Id,
            QuestionId = question.Id,
            AnswerText = text,
            Timestamp = DateTime.UtcNow
        };

    public sealed class PostgresRetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create()
            => new NpgsqlRetryingExecutionStrategy(dependencies, 2, TimeSpan.Zero, null);
    }

    private sealed class SerializationFailureInterceptor : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }
        public int CommitAttempts { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            CommitAttempts++;
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new PostgresException("Simulated serialization failure", "ERROR", "ERROR", "40001");
            }
            return ValueTask.FromResult(result);
        }
    }
}
