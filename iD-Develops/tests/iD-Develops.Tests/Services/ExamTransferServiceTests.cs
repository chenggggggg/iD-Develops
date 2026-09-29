using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace iD_Develops.Tests.Services;

public sealed class ExamTransferServiceTests
{
    [Fact]
    public async Task ExportAndImport_CreatesIndependentDraftWithNewIds()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var sourceExam = new Exam
        {
            Name = "Dutch level test",
            CreatedByUserId = "source-owner",
            Difficulty = DifficultyLevel.B1,
            TimeLimit = 45,
            IntroductionPrimaryLanguage = "<p>Welcome</p>",
            IntroductionSecondaryLanguage = "<p>Welkom</p>",
            CompletionTextPrimary = "Completed",
            ResultGradeDisplayMode = ResultGradeDisplayMode.GradeBands,
            MaxAttempts = -1,
            PublicSlug = "level-test",
            PublishStatus = ExamPublishStatus.Published,
            GradeBands =
            [
                new ExamGradeBand
                {
                    MinimumScore = 10,
                    LabelPrimary = "B1",
                    LabelSecondary = "B1"
                }
            ],
            Questions =
            [
                new MultipleChoiceQuestion
                {
                    QuestionNumber = 1,
                    Text = "Choose the correct answer.",
                    Score = 1,
                    MultipleChoiceAnswer = new MultipleChoiceAnswer
                    {
                        AnswerA = "One",
                        AnswerB = "Two"
                    },
                    CorrectAnswers =
                    [
                        new CorrectAnswer { Text = "One", Score = 1 }
                    ]
                },
                new OpenQuestion
                {
                    QuestionNumber = 2,
                    Text = "Write an answer.",
                    Score = 2,
                    CorrectAnswers =
                    [
                        new CorrectAnswer { Text = "Answer", Score = 2 }
                    ]
                }
            ]
        };

        dbContext.Exams.Add(sourceExam);
        await dbContext.SaveChangesAsync();
        var sourceQuestionIds = sourceExam.Questions.Select(q => q.Id).ToHashSet();

        var service = new ExamTransferService(dbContext);
        var export = await service.ExportAsync(sourceExam.Id);

        Assert.NotNull(export);
        Assert.EndsWith(".exam.json", export.FileName, StringComparison.Ordinal);

        await using var stream = new MemoryStream(export.Content);
        var import = await service.ImportAsync(stream, "import-owner");

        Assert.True(import.Success);
        Assert.NotNull(import.ExamId);
        Assert.NotEqual(sourceExam.Id, import.ExamId.Value);
        Assert.Contains(import.Warnings!, warning => warning.Contains("public slug", StringComparison.OrdinalIgnoreCase));

        dbContext.ChangeTracker.Clear();
        var imported = await dbContext.Exams
            .Include(e => e.GradeBands)
            .FirstAsync(e => e.Id == import.ExamId.Value);
        var importedQuestions = await dbContext.Questions
            .Include(q => q.CorrectAnswers)
            .Include("MultipleChoiceAnswer")
            .Where(q => q.ExamId == imported.Id)
            .OrderBy(q => q.QuestionNumber)
            .ToListAsync();

        Assert.Equal("Dutch level test", imported.Name);
        Assert.Equal("import-owner", imported.CreatedByUserId);
        Assert.Equal(ExamPublishStatus.Draft, imported.PublishStatus);
        Assert.Null(imported.PublicSlug);
        Assert.Single(imported.GradeBands);
        Assert.Equal(2, importedQuestions.Count);
        Assert.DoesNotContain(importedQuestions, question => sourceQuestionIds.Contains(question.Id));

        var multipleChoice = Assert.IsType<MultipleChoiceQuestion>(importedQuestions[0]);
        Assert.Equal("One", multipleChoice.MultipleChoiceAnswer.AnswerA);
        Assert.Single(multipleChoice.CorrectAnswers);
        Assert.IsType<OpenQuestion>(importedQuestions[1]);
    }

    [Fact]
    public async Task Import_InvalidJson_DoesNotCreateExam()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new ExamTransferService(dbContext);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not-json"));

        var result = await service.ImportAsync(stream, "owner");

        Assert.False(result.Success);
        Assert.Equal("Reading JSON", result.FailureStage);
        Assert.NotNull(result.DiagnosticDetails);
        Assert.Contains(result.DiagnosticDetails, detail => detail.Contains("Path $", StringComparison.Ordinal));
        Assert.Contains(result.DiagnosticDetails, detail => detail.Contains("line 1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, await dbContext.Exams.CountAsync());
    }

    [Fact]
    public async Task Import_UnsupportedFormatVersion_DoesNotCreateExam()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new ExamTransferService(dbContext);
        var package = new ExamTransferPackage
        {
            FormatVersion = ExamTransferPackage.CurrentFormatVersion + 1,
            Exam = new ExamTransferDefinition
            {
                Name = "Future exam",
                DifficultyValue = (int)DifficultyLevel.A1,
                IntroductionPrimaryLanguage = string.Empty
            }
        };
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(package));

        var result = await service.ImportAsync(stream, "owner");

        Assert.False(result.Success);
        Assert.Equal("Validating exam definition", result.FailureStage);
        Assert.Contains("format version", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.DiagnosticDetails!, detail => detail.Contains("format version", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, await dbContext.Exams.CountAsync());
    }

    [Fact]
    public async Task Import_InvalidQuestionSetting_IdentifiesQuestionAndSetting()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new ExamTransferService(dbContext);
        var package = new ExamTransferPackage
        {
            Exam = new ExamTransferDefinition
            {
                Name = "Invalid exam",
                DifficultyValue = (int)DifficultyLevel.A1,
                IntroductionPrimaryLanguage = string.Empty,
                Questions =
                [
                    new ExamTransferQuestion
                    {
                        Type = "Open",
                        QuestionNumber = 7,
                        Text = "Question",
                        Score = -1
                    }
                ]
            }
        };
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(package));

        var result = await service.ImportAsync(stream, "owner");

        Assert.False(result.Success);
        Assert.Equal("Validating exam definition", result.FailureStage);
        Assert.Contains("Question 7 score", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(0, await dbContext.Exams.CountAsync());
    }
}
