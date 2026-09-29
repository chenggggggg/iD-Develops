using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class ExamVersionServiceTests
{
    [Fact]
    public async Task PublishVersionAsync_CreatesImmutableSnapshotThatSurvivesLiveEdits()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var exam = new Exam
        {
            Name = "Dutch placement",
            CreatedByUserId = "teacher-1",
            PublishStatus = ExamPublishStatus.Draft,
            DifficultyValue = (int)DifficultyLevel.A1,
            IntroductionPrimaryLanguage = "Welcome",
            CompletionTextPrimary = "Done",
            MaxAttempts = 2,
            Questions = new List<Question>()
        };

        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var question = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 1,
            Text = "Original question text",
            CorrectAnswers =
            [
                new CorrectAnswer { Text = "Original answer", Score = 10 }
            ]
        };

        dbContext.Questions.Add(question);
        await dbContext.SaveChangesAsync();

        var service = new ExamVersionService(dbContext);

        var publishResult = await service.PublishVersionAsync(exam, new List<Question> { question });

        Assert.True(publishResult.Success);

        var firstVersion = await dbContext.ExamVersions
            .OrderBy(v => v.VersionNumber)
            .FirstAsync();

        question.Text = "Mutated live question text";
        question.CorrectAnswers.Clear();
        question.CorrectAnswers.Add(new CorrectAnswer { Text = "Mutated answer", Score = 5 });
        await dbContext.SaveChangesAsync();

        var snapshotQuestion = await service.GetQuestionForTakeAsync(exam.Id, question.Id, firstVersion.Id);
        var evaluationExam = await service.GetExamForEvaluationAsync(exam.Id, firstVersion.Id);

        Assert.NotNull(snapshotQuestion);
        Assert.Equal("Original question text", snapshotQuestion!.Text);

        var evaluatedQuestion = Assert.Single(evaluationExam!.Questions);
        Assert.Equal("Original question text", evaluatedQuestion.Text);
        Assert.Equal("Original answer", Assert.Single(evaluatedQuestion.CorrectAnswers).Text);
    }

    [Fact]
    public async Task GetPublishedDescriptorAsync_ReturnsLatestPublishedVersion()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var course = new Course { Name = "General English" };

        var exam = new Exam
        {
            Name = "Level test",
            CreatedByUserId = "teacher-1",
            PublishStatus = ExamPublishStatus.Draft,
            DifficultyValue = (int)DifficultyLevel.A1,
            TimeLimit = 20,
            IntroductionPrimaryLanguage = "Intro v1",
            IntroductionSecondaryLanguage = "Secondary v1",
            CompletionTextPrimary = "Done",
            MaxAttempts = 1,
            PublicSlug = "level-test-v1",
            Course = course,
            Questions = new List<Question>()
        };

        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var question = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 1,
            Text = "Question v1",
            CorrectAnswers =
            [
                new CorrectAnswer { Text = "Answer v1", Score = 10 }
            ]
        };

        dbContext.Questions.Add(question);
        await dbContext.SaveChangesAsync();

        var service = new ExamVersionService(dbContext);

        await service.PublishVersionAsync(exam, new List<Question> { question });

        exam.Name = "Updated level test";
        exam.TimeLimit = 45;
        exam.IntroductionPrimaryLanguage = "Intro v2";
        exam.IntroductionSecondaryLanguage = "Secondary v2";
        exam.MaxAttempts = -1;
        exam.PublicSlug = "level-test-v2";
        course.Name = "Business English";
        question.Text = "Question v2";
        question.CorrectAnswers.Clear();
        question.CorrectAnswers.Add(new CorrectAnswer { Text = "Answer v2", Score = 10 });
        await dbContext.SaveChangesAsync();

        await service.PublishVersionAsync(exam, new List<Question> { question });

        var descriptor = await service.GetPublishedDescriptorAsync(exam.Id);

        Assert.NotNull(descriptor);
        Assert.Equal("Updated level test", descriptor!.ExamTitle);
        Assert.Equal("Business English", descriptor.CourseName);
        Assert.Equal(45, descriptor.TimeLimit);
        Assert.Equal("Intro v2", descriptor!.IntroductionPrimary);
        Assert.Equal("Secondary v2", descriptor.IntroductionSecondary);
        Assert.Equal(2, descriptor.ExamVersionId);
        Assert.Equal(-1, descriptor.MaxAttempts);
        Assert.Equal("level-test-v2", descriptor.PublicSlug);
    }

    [Fact]
    public async Task GetPublishedDescriptorAsync_ReturnsNullAfterExamIsUnpublished()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var exam = new Exam
        {
            Name = "Placement test",
            CreatedByUserId = "teacher-1",
            PublishStatus = ExamPublishStatus.Draft,
            DifficultyValue = (int)DifficultyLevel.A1,
            IntroductionPrimaryLanguage = "Published introduction",
            CompletionTextPrimary = "Done",
            MaxAttempts = 1,
            Questions = new List<Question>()
        };

        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var service = new ExamVersionService(dbContext);
        await service.PublishVersionAsync(exam, Array.Empty<Question>());
        var publishedVersionId = await dbContext.ExamVersions
            .Where(version => version.ExamId == exam.Id)
            .Select(version => version.Id)
            .SingleAsync();

        exam.PublishStatus = ExamPublishStatus.Draft;
        exam.IntroductionPrimaryLanguage = "Updated draft introduction";
        exam.TimeLimit = 30;
        exam.MaxAttempts = -1;
        await dbContext.SaveChangesAsync();

        var descriptor = await service.GetPublishedDescriptorAsync(exam.Id);
        var historicalDescriptor = await service.GetPublishedDescriptorAsync(exam.Id, publishedVersionId);

        Assert.Null(descriptor);
        Assert.NotNull(historicalDescriptor);
        Assert.Equal("Published introduction", historicalDescriptor!.IntroductionPrimary);
    }

    [Fact]
    public async Task GetExamForEvaluationAsync_UsesPublishedGradeBandsSnapshotInsteadOfLiveBands()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var exam = new Exam
        {
            Name = "Placement exam",
            CreatedByUserId = "teacher-1",
            PublishStatus = ExamPublishStatus.Draft,
            DifficultyValue = (int)DifficultyLevel.A1,
            IntroductionPrimaryLanguage = "Intro",
            CompletionTextPrimary = "Done",
            MaxAttempts = 1,
            GradeBands =
            [
                new ExamGradeBand { MinimumScore = 0d, LabelPrimary = "Pass" },
                new ExamGradeBand { MinimumScore = 5d, LabelPrimary = "Great" }
            ],
            Questions = new List<Question>()
        };

        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var question = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 1,
            Text = "Original question text",
            Score = 5d,
            CorrectAnswers =
            [
                new CorrectAnswer { Text = "Original answer", Score = 5d }
            ]
        };

        dbContext.Questions.Add(question);
        await dbContext.SaveChangesAsync();

        var service = new ExamVersionService(dbContext);

        var publishResult = await service.PublishVersionAsync(exam, new List<Question> { question });

        Assert.True(publishResult.Success);

        var firstVersion = await dbContext.ExamVersions
            .OrderBy(v => v.VersionNumber)
            .FirstAsync();

        exam.GradeBands.Clear();
        exam.GradeBands.Add(new ExamGradeBand { MinimumScore = 0d, LabelPrimary = "Changed pass" });
        exam.GradeBands.Add(new ExamGradeBand { MinimumScore = 4d, LabelPrimary = "Changed great" });
        await dbContext.SaveChangesAsync();

        var evaluationExam = await service.GetExamForEvaluationAsync(exam.Id, firstVersion.Id);

        Assert.NotNull(evaluationExam);

        var savedBands = evaluationExam!.GradeBands.OrderByDescending(b => b.MinimumScore).ToList();
        Assert.Collection(
            savedBands,
            band =>
            {
                Assert.Equal(5d, band.MinimumScore, precision: 1);
                Assert.Equal("Great", band.LabelPrimary);
            },
            band =>
            {
                Assert.Equal(0d, band.MinimumScore, precision: 1);
                Assert.Equal("Pass", band.LabelPrimary);
            });
    }
}
