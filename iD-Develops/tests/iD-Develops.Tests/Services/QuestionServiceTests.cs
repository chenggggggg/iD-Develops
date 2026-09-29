using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class QuestionServiceTests
{
    [Fact]
    public async Task UpdateQuestionAsync_DefaultsMissingQuestionScoreToOne()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var exam = new Exam
        {
            Name = "Exam",
            CreatedByUserId = "teacher-1",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0,
            MaxAttempts = -1
        };

        var questionOne = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 1,
            Text = "Question 1",
            Score = 2
        };

        var questionTwo = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 2,
            Text = "Question 2",
            Score = 5
        };

        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        questionOne.ExamId = exam.Id;
        questionTwo.ExamId = exam.Id;
        dbContext.OpenQuestions.AddRange(questionOne, questionTwo);
        await dbContext.SaveChangesAsync();

        var service = new QuestionService(dbContext);
        var trackedQuestion = await service.GetQuestionForExamForUpdateAsync(exam.Id, questionOne.Id);

        Assert.NotNull(trackedQuestion);

        await service.ApplyAutosavePatchAsync(
            trackedQuestion!,
            text: "Updated question 1",
            messageBeforeQuestion: null,
            scenario: null,
            feedback: null,
            funFact: null,
            score: 0,
            openCorrectAnswerText: "Correct answer",
            multipleChoiceAnswerA: null,
            multipleChoiceAnswerB: null,
            multipleChoiceAnswerC: null,
            multipleChoiceAnswerD: null,
            multipleChoiceCorrect: null,
            trueOrFalseCorrect: null,
            imageReference: null,
            audioReference: null);

        await service.UpdateQuestionAsync(trackedQuestion);

        var updatedQuestion = await dbContext.OpenQuestions
            .Include(q => q.CorrectAnswers)
            .SingleAsync(q => q.Id == questionOne.Id);
        Assert.Equal(1d, updatedQuestion.Score);
        Assert.Single(updatedQuestion.CorrectAnswers);
        Assert.Equal(1d, updatedQuestion.CorrectAnswers.Single().Score);
    }

    [Fact]
    public async Task DeleteQuestionFromExamAsync_RenumbersRemainingQuestions()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var exam = new Exam
        {
            Name = "Exam",
            CreatedByUserId = "teacher-1",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0,
            MaxAttempts = -1
        };

        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var first = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 1,
            Text = "Question 1",
            Score = 2
        };

        var second = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 2,
            Text = "Question 2",
            Score = 3
        };

        var third = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 3,
            Text = "Question 3",
            Score = null
        };

        dbContext.OpenQuestions.AddRange(first, second, third);
        await dbContext.SaveChangesAsync();

        var service = new QuestionService(dbContext);
        await service.DeleteQuestionFromExamAsync(exam.Id, second.Id);

        var questions = await dbContext.Questions
            .IgnoreQueryFilters()
            .Where(q => q.ExamId == exam.Id)
            .OrderBy(q => q.Id)
            .ToListAsync();

        Assert.True(questions.Single(q => q.Id == second.Id).IsDeleted);
        Assert.Equal(1, questions.Single(q => q.Id == first.Id).QuestionNumber);
        Assert.Equal(2, questions.Single(q => q.Id == third.Id).QuestionNumber);
    }
}
