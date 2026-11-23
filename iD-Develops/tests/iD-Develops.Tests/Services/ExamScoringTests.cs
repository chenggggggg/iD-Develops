using System.Text.Json;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class ExamScoringTests
{
    [Theory]
    [InlineData("Open")]
    [InlineData("MultipleChoice")]
    [InlineData("TrueOrFalse")]
    public void LegacyAnswerAwardsCannotExceedTheQuestionMaximum(string kind)
    {
        var question = CreateQuestion(kind);
        question.Id = 1;
        question.Score = null;
        question.CorrectAnswers = [new() { Text = "A", Score = 2 }];
        var answer = new ParticipantAnswer { QuestionId = 1, AnswerText = "A" };
        var results = new ExamEvaluationService().EvaluateExam(new Exam { Questions = [question] }, [answer]);
        Assert.Equal(1, results.MaxScore);
        Assert.Equal(1, results.Score);
        Assert.True(answer.IsCorrect);
        question.CorrectAnswers.Single().Score = 0.5;
        results = new ExamEvaluationService().EvaluateExam(new Exam { Questions = [question] }, [answer]);
        Assert.Equal(0.5, results.Score);
        Assert.False(answer.IsCorrect);
        question.Score = 2;
        Assert.Equal(2, new ExamEvaluationService().EvaluateExam(new Exam { Questions = [question] }, [answer]).Score);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 2)]
    public void QuestionMaximaCountOnceAndAlternativesDoNotInflateTheExamTotal(bool partial, double expected)
    {
        var questions = new List<Question>();
        var answers = new List<ParticipantAnswer>();
        foreach (var (id, maximum) in new[] { (1, 1d), (2, 2d), (3, 1d) })
        {
            questions.Add(new OpenQuestion
            {
                Id = id,
                Score = maximum,
                OpenAnswerFields = [new() { Key = "word", AcceptedAnswers =
                    [new() { Text = "full", Score = maximum }, new() { Text = "partial", Score = maximum / 2 }] }]
            });
            answers.Add(new ParticipantAnswer
            {
                QuestionId = id,
                AnswerText = JsonSerializer.Serialize(new
                { format = "open-answers-v1", answers = new { word = partial ? "partial" : "full" } })
            });
        }
        var results = new ExamEvaluationService().EvaluateExam(new Exam { Questions = questions }, answers);
        Assert.Equal(4, results.MaxScore);
        Assert.Equal(expected, results.Score);
        Assert.All(answers, answer => Assert.Equal(!partial, answer.IsCorrect));
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("MultipleChoice")]
    [InlineData("TrueOrFalse")]
    public async Task InvalidMaximumIsRejectedBeforeAnyQuestionChangesAreSaved(string kind)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var question = CreateQuestion(kind);
        question.Text = "Original";
        question.Score = 1;
        question.QuestionNumber = 1;
        var exam = new Exam { Name = "Scoring validation", CreatedByUserId = "owner", IntroductionPrimaryLanguage = "Intro", Questions = [question] };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var service = new ExamEditMutationService(new ExamService(db), new QuestionService(db), null!, null!);
        foreach (var maximum in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            var result = await service.SaveQuestionAsync(new SaveQuestionCommand
            {
                ExamId = exam.Id,
                QuestionId = question.Id,
                QuestionNumber = 1,
                Kind = kind,
                Score = maximum,
                Text = "Must not save"
            }, "owner", true);
            Assert.False(result.Success);
            Assert.Equal(400, result.StatusCode);
            var saved = await db.Questions.AsNoTracking().SingleAsync(item => item.Id == question.Id);
            Assert.Equal("Original", saved.Text);
            Assert.Equal(1, saved.Score);
        }
    }

    private static Question CreateQuestion(string kind) => kind switch
    {
        "Open" => new OpenQuestion(),
        "MultipleChoice" => new MultipleChoiceQuestion(),
        "TrueOrFalse" => new TrueOrFalseQuestion(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
