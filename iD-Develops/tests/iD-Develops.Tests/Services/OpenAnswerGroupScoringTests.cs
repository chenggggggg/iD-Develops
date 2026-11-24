using System.Text.Json;
using System.Text.Json.Serialization;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class OpenAnswerGroupScoringTests
{
    [Fact]
    public void SmallFractionalAwardsDoNotTurnPartialCreditIntoFullCredit()
    {
        var question = new OpenQuestion
        {
            Id = 1,
            OpenAnswerFields = [new() { Key = "word", AcceptedAnswers =
            [new() { Text = "full", Score = 0.000000001 }, new() { Text = "partial", Score = 0.0000000001 }] }]
        };
        var answer = new ParticipantAnswer
        {
            QuestionId = 1,
            AnswerText = JsonSerializer.Serialize(new
            { format = "open-answers-v1", answers = new { word = "partial" } })
        };
        var result = new ExamEvaluationService().EvaluateExam(new Exam { Questions = [question] }, [answer]);
        Assert.Equal(0.0000000001, result.Score);
        Assert.Equal(0.000000001, result.MaxScore);
        Assert.False(answer.IsCorrect);
    }

    [Theory]
    [InlineData("om", "te", 1, true)]
    [InlineData("OM.", " te ", 1, true)]
    [InlineData("om", "", 0.5, false)]
    [InlineData("wrong", "te", 0.5, false)]
    [InlineData("", "", 0, false)]
    public void TwoHalfPointInputsSumToOneEvenWhenCachedQuestionPointsDiffer(string first, string second, double expected, bool correct)
    {
        var question = new OpenQuestion
        {
            Id = 1,
            Score = 99,
            OpenAnswerFields = [new() { Key = "first", AcceptedAnswers = [new() { Text = "om", Score = 0.5 }] },
                new() { Key = "second", AcceptedAnswers = [new() { Text = "te", Score = 0.5 }] }]
        };
        var answer = new ParticipantAnswer { QuestionId = 1, AnswerText = JsonSerializer.Serialize(new { format = "open-answers-v1", answers = new { first, second } }) };
        var result = new ExamEvaluationService().EvaluateExam(new Exam { Questions = [question] }, [answer]);
        Assert.Equal(1, result.MaxScore);
        Assert.Equal(expected, result.Score);
        Assert.Equal(expected, answer.AwardedScore);
        Assert.Equal(correct, answer.IsCorrect);
        Assert.Equal(99, question.Score);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 2)]
    public void HighestAlternativeDefinesEachInputMaximumWhileOnlyOneAlternativeCanEarnPoints(bool partial, double expected)
    {
        var question = new OpenQuestion { Score = 1 };
        foreach (var (key, maximum) in new[] { ("first", 1d), ("second", 2d), ("third", 1d) })
            question.OpenAnswerFields.Add(new()
            {
                Key = key,
                AcceptedAnswers = [new() { Text = "full", Score = maximum },
                new() { Text = "FULL", Score = maximum / 2 }, new() { Text = "partial", Score = maximum / 2 }]
            });
        var submission = JsonSerializer.Serialize(new { format = "open-answers-v1", answers = question.OpenAnswerFields.ToDictionary(field => field.Key, _ => partial ? "partial" : "full") });
        Assert.Equal(4, OpenAnswerService.MaximumScore(question));
        Assert.Equal(expected, OpenAnswerService.Evaluate(question, submission));
        Assert.Equal(4, ExamScoring.TotalMaximum([question]));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ImportsMaterializeLegacyDefaultsAndExportExplicitAdditiveAwards(int format)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var package = new ExamTransferPackage
        {
            FormatVersion = format,
            Exam = new()
            {
                Name = "Input points",
                IntroductionPrimaryLanguage = "Intro",
                Questions = [new() { Type = "Open", QuestionNumber = 1, Text = "Two words", Score = 1,
                OpenAnswerFields = [new() { Key = "first", AcceptedAnswers = [new() { Text = "om" }] },
                    new() { Key = "second", AcceptedAnswers = [new() { Text = "te" }] }] }]
            }
        };
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(package));
        var service = new ExamTransferService(db);
        var imported = await service.ImportAsync(stream, "owner");
        Assert.True(imported.Success, imported.ErrorMessage);
        var question = await db.OpenQuestions.SingleAsync();
        Assert.Equal(1, question.Score);
        Assert.All(question.OpenAnswerFields, field => Assert.Equal(0.5, field.AcceptedAnswers.Single().Score));
        var exported = await service.ExportAsync(imported.ExamId!.Value);
        var roundTrip = JsonSerializer.Deserialize<ExamTransferPackage>(exported!.Content,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } })!;
        Assert.Equal(4, roundTrip.FormatVersion);
        Assert.All(roundTrip.Exam.Questions.Single().OpenAnswerFields, field => Assert.Equal(0.5, field.AcceptedAnswers.Single().Score));
    }

    [Fact]
    public async Task SavingUsesDerivedPointsInsteadOfThePostedQuestionMaximum()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var question = new OpenQuestion { QuestionNumber = 1, Text = "Two blanks", Score = 1 };
        var exam = new Exam { Name = "Derived points", CreatedByUserId = "owner", IntroductionPrimaryLanguage = "Intro", Questions = [question] };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var fields = new List<OpenAnswerField> { new() { Key = "first", AcceptedAnswers = [new() { Text = "om", Score = 0.5 }] },
            new() { Key = "second", AcceptedAnswers = [new() { Text = "te", Score = 0.5 }] } };
        var service = new ExamEditMutationService(new ExamService(db), new QuestionService(db), null!, null!);
        var result = await service.SaveQuestionAsync(new SaveQuestionCommand
        {
            ExamId = exam.Id,
            QuestionId = question.Id,
            QuestionNumber = 1,
            Kind = "Open",
            Score = 99,
            OpenAnswerFieldsJson = OpenAnswerFieldJson.Serialize(fields)
        }, "owner", true);
        Assert.True(result.Success, result.ErrorMessage);
        var saved = await db.OpenQuestions.AsNoTracking().SingleAsync();
        Assert.Equal(1, saved.Score);
        Assert.Equal(1, ExamScoring.TotalMaximum([saved]));
    }
}
