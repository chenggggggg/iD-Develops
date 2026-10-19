using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Pages.Examination;
using iD_Develops.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace iD_Develops.E2ETests;

internal sealed class ExamAttemptFixture(ApplicationDbContext db) : IAsyncDisposable
{
    public Exam Exam { get; private set; } = null!;
    public iD_Develops.Models.Record Record { get; private set; } = null!;

    public static async Task<ExamAttemptFixture> CreateAsync(bool expired = true, bool savedAnswer = true, bool includeUnansweredQuestion = false)
    {
        var fixture = new ExamAttemptFixture(LocalTestApplication.CreateDbContext());
        try
        {
            await fixture.SeedAsync(expired, savedAnswer, includeUnansweredQuestion);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task SeedAsync(bool expired, bool savedAnswer, bool includeUnansweredQuestion)
    {
        var question = new OpenQuestion
        {
            QuestionNumber = 1,
            Text = "Where do you live?",
            Score = 2,
            CorrectAnswers = [new CorrectAnswer { Text = "Amsterdam" }]
        };
        Exam = new Exam
        {
            Name = $"E2E expiry {Guid.NewGuid():N}",
            CreatedByUserId = "e2e-expiry-teacher",
            IntroductionPrimaryLanguage = "Answer the question.",
            PublishStatus = ExamPublishStatus.Published,
            TimeLimitSeconds = 120,
            Questions = [question]
        };
        if (includeUnansweredQuestion)
        {
            Exam.Questions.Add(new OpenQuestion
            {
                QuestionNumber = 2,
                Text = "A question left blank",
                Score = 3,
                CorrectAnswers = [new CorrectAnswer { Text = "Utrecht" }]
            });
        }
        db.Exams.Add(Exam);
        await db.SaveChangesAsync();

        // A dedicated published snapshot avoids replacing the locally configured public level test.
        var version = new ExamVersion
        {
            Exam = Exam,
            VersionNumber = 1,
            Name = Exam.Name,
            IntroductionPrimaryLanguage = Exam.IntroductionPrimaryLanguage,
            TimeLimitSeconds = Exam.TimeLimitSeconds,
            PublicSlug = "level-test",
            PublishedAtUtc = DateTime.UtcNow,
            Questions = Exam.Questions.Select(source => new ExamVersionQuestion
            {
                SourceQuestionId = source.Id,
                QuestionType = "Open",
                QuestionNumber = source.QuestionNumber,
                Text = source.Text,
                Score = source.Score,
                CorrectAnswers = source.CorrectAnswers.Select(answer => new ExamVersionCorrectAnswer { Text = answer.Text }).ToList()
            }).ToList()
        };
        var now = DateTime.UtcNow;
        Record = new iD_Develops.Models.Record
        {
            Exam = Exam,
            ExamVersion = version,
            ExamStatus = ExamStatus.InProgress,
            StartDateTime = expired ? now.AddMinutes(-10) : now.AddSeconds(-10),
            EndDateTime = expired ? now.AddMinutes(-1) : now.AddSeconds(90),
            StatusReason = RecordStatusReason.Started,
            LastActivityUtc = expired ? now.AddMinutes(-5) : now.AddSeconds(-5)
        };
        db.Records.Add(Record);
        if (savedAnswer)
        {
            db.ParticipantAnswers.Add(new ParticipantAnswer
            {
                Record = Record,
                Question = question,
                AnswerText = "Amsterdam",
                Timestamp = Record.StartDateTime.AddSeconds(1)
            });
        }
        await db.SaveChangesAsync();
    }

    public string AttemptUrl(string culture)
        => $"{LocalTestApplication.PublicBaseUrl}/{culture}/examination/level-test/{Record.Id}";

    public string CompletionUrl(string culture)
        => $"{LocalTestApplication.PublicBaseUrl}/{culture}/examination/completed/{Record.Id}";

    public string ResultsUrl(string culture)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(LocalTestApplication.DatabaseConnectionString));
        services.AddDataProtection().SetApplicationName("iD-Develops").PersistKeysToDbContext<ApplicationDbContext>();
        using var provider = services.BuildServiceProvider();
        var token = ResultsModel.CreatePublicResultToken(provider.GetRequiredService<IDataProtectionProvider>(),
            Record.Id, "expiry-learner@example.test");
        return $"{LocalTestApplication.PublicBaseUrl}/{culture}/examination/results/{Record.Id}?token={Uri.EscapeDataString(token)}";
    }

    public Task ExpireAsync()
        => db.Records.Where(record => record.Id == Record.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(record => record.EndDateTime, DateTime.UtcNow.AddMinutes(-1)));

    public Task<iD_Develops.Models.Record> ReadRecordAsync()
        => db.Records.AsNoTracking().SingleAsync(record => record.Id == Record.Id);

    public Task<List<ApplicationAuditEvent>> ReadTransitionsAsync()
        => db.ApplicationAuditEvents.AsNoTracking().Where(audit =>
            audit.EntityId == Record.Id.ToString() && audit.EventType == ApplicationAuditEventTypes.ExamAttemptStatusChanged).ToListAsync();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Record != null)
            {
                await db.ParticipantAnswers.Where(answer => answer.RecordId == Record.Id).ExecuteDeleteAsync();
                await db.Records.Where(record => record.Id == Record.Id).ExecuteDeleteAsync();
                await db.ApplicationAuditEvents.Where(audit => audit.EntityType == nameof(iD_Develops.Models.Record) &&
                    audit.EntityId == Record.Id.ToString()).ExecuteDeleteAsync();
            }
            if (Exam != null && Exam.Id > 0)
            {
                await db.ExamLogs.Where(log => log.ExamId == Exam.Id).ExecuteDeleteAsync();
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
