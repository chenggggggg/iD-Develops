using System.Text.Json;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class ExamIncrementalVersionTests
{
    [Fact]
    public async Task TimeLimitOnlyChange_AddsOneDeltaRow_AndReusesUnchangedVersion()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = Definition();
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var service = new ExamVersionService(db);
        var first = (await service.GetOrCreateAttemptVersionAsync(exam.Id))!;
        exam.TimeLimitSeconds = 2400;
        await db.SaveChangesAsync();

        // Reading the launch screen does not create versions for every autosave.
        Assert.Equal(2400, (await service.GetPublishedDescriptorAsync(exam.Id))!.DurationSeconds);
        Assert.Single(await db.ExamVersions.ToListAsync());
        var second = (await service.GetOrCreateAttemptVersionAsync(exam.Id))!;
        var repeated = (await service.GetOrCreateAttemptVersionAsync(exam.Id))!;

        Assert.Equal(second.ExamVersionId, repeated.ExamVersionId);
        Assert.Equal(2, await db.ExamVersions.CountAsync());
        var delta = await db.ExamVersions.SingleAsync(v => v.Id == second.ExamVersionId);
        Assert.Equal(first.ExamVersionId, delta.ParentVersionId);
        using var json = JsonDocument.Parse(delta.ChangesJson!);
        var change = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("TimeLimitSeconds", Assert.Single(change.GetProperty("Path").EnumerateArray()).GetString());
        Assert.Equal(2400, change.GetProperty("Value").GetInt32());
        Assert.Equal(0, await db.ExamVersionQuestions.CountAsync());
        Assert.Equal(0, await db.ExamVersionCorrectAnswers.CountAsync());
        Assert.Equal(0, await db.ExamVersionGradeBands.CountAsync());
        Assert.Equal(3600, (await service.GetPublishedDescriptorAsync(exam.Id, first.ExamVersionId))!.DurationSeconds);
        Assert.Equal(2400, (await service.GetPublishedDescriptorAsync(exam.Id, second.ExamVersionId))!.DurationSeconds);
        Assert.Equal(ExamPublishStatus.Published, exam.PublishStatus);
    }

    [Fact]
    public async Task QuestionsOptionsAnswersAndGrades_KeepTheirExactHistoricalValues()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = Definition();
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var service = new ExamVersionService(db);
        var first = (await service.GetOrCreateAttemptVersionAsync(exam.Id))!;
        var question = (MultipleChoiceQuestion)exam.Questions.First();
        question.Text = "Changed question";
        question.MultipleChoiceAnswer!.AnswerB = "Changed option";
        question.ImageReference = "exams/new-image";
        question.CorrectAnswers.First(a => a.Text == "A").Score = 1;
        exam.GradeBands.First().LabelPrimary = "Changed grade";
        exam.IntroductionSecondaryLanguage = null;
        exam.Questions.Last().IsDeleted = true;
        await db.SaveChangesAsync();
        var second = (await service.GetOrCreateAttemptVersionAsync(exam.Id))!;

        // A fresh service ensures this is reconstructed from persisted deltas.
        service = new ExamVersionService(db);
        var original = (await service.GetExamForEvaluationAsync(exam.Id, first.ExamVersionId))!;
        var updated = (await service.GetExamForEvaluationAsync(exam.Id, second.ExamVersionId))!;
        Assert.Equal(2, original.Questions.Count);
        var originalQuestion = Assert.IsType<MultipleChoiceQuestion>(original.Questions.First());
        Assert.Equal("Original question", originalQuestion.Text);
        Assert.Equal("Original option", originalQuestion.MultipleChoiceAnswer!.AnswerB);
        Assert.Equal("exams/original-image", originalQuestion.ImageReference);
        Assert.Equal(2d, originalQuestion.CorrectAnswers.Single(a => a.Text == "A").Score);
        Assert.Equal("Original grade", Assert.Single(original.GradeBands).LabelPrimary);
        Assert.Equal("Secondary introduction", original.IntroductionSecondaryLanguage);
        Assert.Single(updated.Questions);
        var changed = Assert.IsType<MultipleChoiceQuestion>(updated.Questions.Single());
        Assert.Equal("Changed question", changed.Text);
        Assert.Equal("Changed option", changed.MultipleChoiceAnswer!.AnswerB);
        Assert.Equal(1d, changed.CorrectAnswers.Single(a => a.Text == "A").Score);
        Assert.Equal("Unchanged answer", changed.CorrectAnswers.Single(a => a.Text == "Unchanged answer").Text);
        Assert.Null(updated.IntroductionSecondaryLanguage);
        var delta = (await db.ExamVersions.SingleAsync(v => v.Id == second.ExamVersionId)).ChangesJson!;
        Assert.DoesNotContain("Unchanged answer", delta, StringComparison.Ordinal);
        Assert.DoesNotContain("Unchanged question", delta, StringComparison.Ordinal);
        Assert.True(await service.IsMediaReferencedAsync(exam.Id, "exams/original-image"));
        Assert.True(await service.IsMediaReferencedAsync(exam.Id, "exams/new-image"));
        Assert.False(await service.IsMediaReferencedAsync(exam.Id, "exams/unused-image"));
        var taking = (await service.GetQuestionForTakeAsync(exam.Id, question.Id, first.ExamVersionId))!;
        Assert.Empty(taking.CorrectAnswers);
        Assert.Empty(taking.Feedback!);
    }

    [Fact]
    public async Task LegacyFullSnapshot_CanBeParentOfAnUntimedDeltaWithoutCopyingContent()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = Definition();
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        // Existing relational snapshots remain untouched by the migration.
        var legacy = new ExamVersion
        {
            ExamId = exam.Id,
            VersionNumber = 1,
            Name = "Legacy title",
            TimeLimitSeconds = 3600,
            IntroductionPrimaryLanguage = "Original intro",
            MaxAttempts = 2,
            PublishedAtUtc = DateTime.UtcNow,
            Questions = [new ExamVersionQuestion
            {
                SourceQuestionId = exam.Questions.First().Id, QuestionType = "Open", QuestionNumber = 1,
                Text = "Legacy question", CorrectAnswers = [new ExamVersionCorrectAnswer { Text = "Legacy answer", Score = 2 }]
            }]
        };
        db.ExamVersions.Add(legacy);
        await db.SaveChangesAsync();
        exam.TimeLimitSeconds = null;
        var service = new ExamVersionService(db);
        await db.SaveChangesAsync();
        var latest = (await service.GetOrCreateAttemptVersionAsync(exam.Id))!;
        Assert.Equal(legacy.Id, (await db.ExamVersions.SingleAsync(v => v.Id == latest.ExamVersionId)).ParentVersionId);
        Assert.Equal(0, latest.DurationSeconds);
        Assert.Equal(1, await db.ExamVersionQuestions.CountAsync());
        Assert.Equal(1, await db.ExamVersionCorrectAnswers.CountAsync());
        var preserved = (await service.GetExamForEvaluationAsync(exam.Id, legacy.Id))!;
        Assert.Equal("Legacy title", preserved.Name);
        Assert.Equal("Legacy answer", Assert.Single(Assert.Single(preserved.Questions).CorrectAnswers).Text);
        Assert.Equal(3600, preserved.TimeLimitSeconds);
        Assert.Null((await service.GetExamForEvaluationAsync(exam.Id, latest.ExamVersionId))!.TimeLimitSeconds);
    }

    [Fact]
    public async Task VisibilityChanges_PreserveVersions_AndPreventStartingUnpublishedExams()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = Definition();
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var service = new ExamVersionService(db);
        var first = (await service.GetOrCreateAttemptVersionAsync(exam.Id))!;
        exam.PublishStatus = ExamPublishStatus.Archived;
        await db.SaveChangesAsync();
        Assert.Null(await service.GetOrCreateAttemptVersionAsync(exam.Id));
        Assert.NotNull(await service.GetPublishedDescriptorAsync(exam.Id, first.ExamVersionId));
        exam.PublishStatus = ExamPublishStatus.Published;
        await db.SaveChangesAsync();
        await service.GetOrCreateAttemptVersionAsync(exam.Id);
        Assert.Single(await db.ExamVersions.ToListAsync());
        Assert.Equal(first.ExamVersionId, (await service.GetOrCreateAttemptVersionAsync(exam.Id))!.ExamVersionId);
    }

    [Fact]
    public async Task MediaReplacementAndRemoval_PreserveObjectsUsedByEarlierAttempts()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = Definition();
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var versions = new ExamVersionService(db);
        var first = (await versions.GetOrCreateAttemptVersionAsync(exam.Id))!;
        var storage = new RecordingStorage();
        var mutations = new ExamEditMutationService(new ExamService(db), new QuestionService(db), versions, storage);
        var question = exam.Questions.First();
        var upload = await mutations.CreateUploadAsync(exam.Id, question.Id, "image.webp", "image/webp", "owner", true);
        var replacement = await mutations.CreateUploadAsync(exam.Id, question.Id, "image.webp", "image/webp", "owner", true);
        Assert.True(upload.Success);
        Assert.True(replacement.Success);
        Assert.NotEqual(upload.ObjectKey, replacement.ObjectKey);

        Assert.True((await mutations.DeleteQuestionFileAsync(exam.Id, question.Id, "image", "owner", true)).Success);
        Assert.DoesNotContain("exams/original-image", storage.Deleted);
        question.ImageReference = replacement.ObjectKey;
        await db.SaveChangesAsync();
        Assert.True((await mutations.DeleteQuestionFileAsync(exam.Id, question.Id, "image", "owner", true)).Success);
        Assert.Contains(replacement.ObjectKey!, storage.Deleted);
        var original = (await versions.GetExamForEvaluationAsync(exam.Id, first.ExamVersionId))!;
        Assert.Equal("exams/original-image", original.Questions.First().ImageReference);
    }

    private sealed class RecordingStorage : IPresignedUrlService
    {
        public List<string> Deleted { get; } = [];
        public Task DeleteObjectAsync(string objectKey)
        {
            Deleted.Add(objectKey);
            return Task.CompletedTask;
        }
        public Task<string> GeneratePresignedReadUrl(string objectKey) => Task.FromResult($"https://storage.test/{objectKey}");
        public Task<string> GeneratePresignedUrl(string objectKey, string contentType, string? cacheControl = null)
            => Task.FromResult($"https://storage.test/{objectKey}");
    }

    private static Exam Definition() => new()
    {
        Name = "Versioned exam",
        CreatedByUserId = "owner",
        PublishStatus = ExamPublishStatus.Published,
        TimeLimitSeconds = 3600,
        MaxAttempts = 2,
        IntroductionPrimaryLanguage = "Original intro",
        IntroductionSecondaryLanguage = "Secondary introduction",
        PublicSlug = "level-test",
        GradeBands = [new ExamGradeBand { MinimumScore = 0, LabelPrimary = "Original grade" }],
        Questions =
        [
            new MultipleChoiceQuestion
            {
                QuestionNumber = 1, Text = "Original question", Score = 2, ImageReference = "exams/original-image",
                MultipleChoiceAnswer = new MultipleChoiceAnswer { AnswerA = "A", AnswerB = "Original option", AnswerC = "C", AnswerD = "D" },
                CorrectAnswers = [new CorrectAnswer { Text = "A", Score = 2 }, new CorrectAnswer { Text = "Unchanged answer", Score = 1 }]
            },
            new OpenQuestion { QuestionNumber = 2, Text = "Unchanged question", CorrectAnswers = [new CorrectAnswer { Text = "Unchanged" }] }
        ]
    };
}
