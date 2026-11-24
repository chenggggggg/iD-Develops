using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class OpenAnswerTests
{
    [Theory]
    [InlineData("om", "te", 1)]
    [InlineData(" OM ", "Te.", 1)]
    [InlineData("om", "", 0.5)]
    [InlineData("", "te", 0.5)]
    [InlineData("te", "om", 0)]
    [InlineData("wrong", "te", 0.5)]
    public void MultipleInputsEarnIndependentCreditWithoutMultiplyingLegacyFullAwards(string first, string second, double expected)
    {
        var question = PairedQuestion();
        var participant = new ParticipantAnswer { QuestionId = question.Id, AnswerText = Submission(first, second) };
        var evaluation = new ExamEvaluationService().EvaluateExam(new Exam { Questions = [question] }, [participant]);
        Assert.Equal(expected, evaluation.Score);
        Assert.Equal(1, evaluation.MaxScore);
        Assert.Equal(expected == 1, participant.IsCorrect);
        Assert.Equal(expected, participant.AwardedScore);
    }

    [Theory]
    [InlineData("de zonnebril", 1)]
    [InlineData("EEN   zonnebril", 1)]
    [InlineData("zonnebril", 0.5)]
    [InlineData("het zonnebril", 0.5)]
    [InlineData("zonnenbril", 0)]
    public void AlternativesAndPartialCreditFollowTheAnswerKey(string text, double expected)
    {
        var question = new OpenQuestion
        {
            Score = 1,
            OpenAnswerFields = [new() { Key = "word", AcceptedAnswers =
                [new() { Text = "De zonnebril" }, new() { Text = "een zonnebril" },
                    new() { Text = "zonnebril", Score = 0.5 }, new() { Text = "het zonnebril", Score = 0.5 }] }]
        };
        Assert.Equal(expected, OpenAnswerService.Evaluate(question, JsonSerializer.Serialize(new { format = "open-answers-v1", answers = new { word = text } })));
    }

    [Fact]
    public void SubmissionValidationPreservesPartialInputsAndRejectsForeignKeys()
    {
        var question = PairedQuestion();
        var partial = OpenAnswerService.NormalizeSubmission(question, Submission("om", ""));
        Assert.False(OpenAnswerService.IsComplete(partial));
        Assert.True(OpenAnswerService.TryRead(partial, out var values));
        Assert.Equal("om", values["first"]);
        Assert.Equal("", values["second"]);
        Assert.Equal("", OpenAnswerService.NormalizeSubmission(question, Submission(" ", "")));
        Assert.Throws<FormatException>(() => OpenAnswerService.NormalizeSubmission(question, "om te"));
        Assert.Throws<FormatException>(() => OpenAnswerService.NormalizeSubmission(question,
            "{\"format\":\"open-answers-v1\",\"answers\":{\"first\":\"om\",\"foreign\":\"te\"}}"));
        Assert.False(OpenAnswerService.TryRead("{\"format\":\"open-answers-v1\",\"answers\":{\"first\":\"om\",\"first\":\"te\"}}", out _));
        Assert.True(OpenAnswerService.IsComplete("Legacy open answer"));
    }

    [Fact]
    public void InvalidConfigurationsCannotDefineDuplicateInputsOrInvalidPoints()
    {
        var fields = PairedQuestion().OpenAnswerFields;
        fields[1].Key = fields[0].Key;
        Assert.Throws<ValidationException>(() => OpenAnswerService.Validate(fields, 1));
        fields[1].Key = "second";
        fields[0].AcceptedAnswers[0].Score = -1;
        Assert.Throws<ValidationException>(() => OpenAnswerService.Validate(fields, 1));
        fields[0].AcceptedAnswers[0].Score = double.NaN;
        Assert.Throws<ValidationException>(() => OpenAnswerService.Validate(fields, 1));
    }

    [Fact]
    public void FullLengthUnicodeInputsRemainReadableAfterServerSerialization()
    {
        var text = new string('é', OpenAnswerService.MaximumAnswerLength);
        var question = new OpenQuestion
        {
            Score = 1,
            OpenAnswerFields = Enumerable.Range(1, OpenAnswerService.MaximumFields)
                .Select(index => new OpenAnswerField { Key = $"field-{index}", AcceptedAnswers = [new() { Text = text }] }).ToList()
        };
        var json = JsonSerializer.Serialize(new { format = "open-answers-v1", answers = question.OpenAnswerFields.ToDictionary(field => field.Key, _ => text) });
        var saved = OpenAnswerService.NormalizeSubmission(question, json);
        Assert.True(OpenAnswerService.TryRead(saved, out var values));
        Assert.All(values.Values, value => Assert.Equal(text, value));
        Assert.Equal(1, OpenAnswerService.Evaluate(question, saved), 12);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task LegacySingleAnswerImportsKeepTheirOriginalScoring(int format)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var package = new ExamTransferPackage
        {
            FormatVersion = format,
            Exam = new()
            {
                Name = "Legacy exam",
                IntroductionPrimaryLanguage = "Intro",
                Questions =
                [new() { Type = "Open", QuestionNumber = 1, Text = "City?", Score = 2, CorrectAnswers = [new() { Text = "Amsterdam" }] }]
            }
        };
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(package));
        var result = await new ExamTransferService(db).ImportAsync(stream, "owner");
        Assert.True(result.Success, result.ErrorMessage);
        var question = await db.OpenQuestions.Include(item => item.CorrectAnswers).SingleAsync();
        Assert.Empty(question.OpenAnswerFields);
        var exam = new Exam { Questions = [question] };
        var evaluator = new ExamEvaluationService();
        Assert.Equal(2, evaluator.EvaluateScore(exam, [new() { QuestionId = question.Id, AnswerText = "Amsterdam" }]));
        Assert.Equal(0, evaluator.EvaluateScore(exam, [new() { QuestionId = question.Id, AnswerText = "amsterdam" }]));
    }

    [Fact]
    public async Task ImportExportAndNestedChangesPersistWithoutAffectingEarlierSnapshots()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var question = PairedQuestion();
        question.Id = 0;
        var exam = new Exam
        {
            Name = "A2 open inputs",
            CreatedByUserId = "owner",
            IntroductionPrimaryLanguage = "Intro",
            PublishStatus = ExamPublishStatus.Published,
            Questions = [question]
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var versions = new ExamVersionService(db);
        var first = (await versions.GetOrCreateAttemptVersionAsync(exam.Id))!;
        question.OpenAnswerFields[0].Label = "Changed first prompt";
        await db.SaveChangesAsync();
        var second = (await versions.GetOrCreateAttemptVersionAsync(exam.Id))!;
        Assert.NotEqual(first.ExamVersionId, second.ExamVersionId);
        var delta = await db.ExamVersions.AsNoTracking().SingleAsync(version => version.Id == second.ExamVersionId);
        Assert.DoesNotContain("\"om\"", delta.ChangesJson);
        Assert.DoesNotContain("\"te\"", delta.ChangesJson);
        db.ChangeTracker.Clear();
        var original = (OpenQuestion)(await versions.GetExamForEvaluationAsync(exam.Id, first.ExamVersionId))!.Questions.Single();
        Assert.Equal("First missing word", original.OpenAnswerFields[0].Label);
        Assert.Equal(1, OpenAnswerService.Evaluate(original, Submission("om", "te")));
        var take = (OpenQuestion)(await versions.GetQuestionForTakeAsync(exam.Id, question.Id, first.ExamVersionId))!;
        Assert.All(take.OpenAnswerFields, field => Assert.Empty(field.AcceptedAnswers));
        Assert.Equal(new[] { "first", "second" }, take.OpenAnswerFields.Select(field => field.Key));
        var liveTake = (OpenQuestion)(await versions.GetQuestionForTakeAsync(exam.Id, question.Id))!;
        Assert.All(liveTake.OpenAnswerFields, field => Assert.Empty(field.AcceptedAnswers));
        var transfer = new ExamTransferService(db);
        var export = (await transfer.ExportAsync(exam.Id))!;
        await using var stream = new MemoryStream(export.Content);
        var imported = await transfer.ImportAsync(stream, "new-owner");
        Assert.True(imported.Success, imported.ErrorMessage);
        var copy = await db.OpenQuestions.SingleAsync(item => item.ExamId == imported.ExamId);
        Assert.Equal("Changed first prompt", copy.OpenAnswerFields[0].Label);
        Assert.Equal(1, OpenAnswerService.Evaluate(copy, Submission("om", "te")));
        Assert.Contains("First missing word: om", OpenAnswerService.FormatAnswer(original, Submission("om", "te"), "Blank"));
        Assert.Contains("Second missing word: te", OpenAnswerService.FormatAcceptedAnswers(original, "No model"));
    }

    [Theory]
    [InlineData(0, "D", true)]
    [InlineData(1, "A", false)]
    [InlineData(2, "B", true)]
    [InlineData(3, "C", true)]
    [InlineData(4, "D", true)]
    [InlineData(2, "D", false)]
    public async Task SourceChoiceCountsCanPublishButInvisibleCorrectOptionsCannot(int count, string correct, bool expected)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var question = new MultipleChoiceQuestion
        {
            QuestionNumber = 1,
            Text = "Choose the correct option.",
            MultipleChoiceAnswer = new()
            {
                AnswerA = count >= 1 ? "One" : null,
                AnswerB = count >= 2 ? "Two" : null,
                AnswerC = count >= 3 ? "Three" : null,
                AnswerD = count >= 4 ? "Four" : null
            },
            CorrectAnswers = [new() { Text = correct }]
        };
        var exam = new Exam { Name = "Source choices", CreatedByUserId = "owner", IntroductionPrimaryLanguage = "Intro", PublishStatus = ExamPublishStatus.Draft, Questions = [question] };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var service = new ExamEditMutationService(new ExamService(db), new QuestionService(db), new ExamVersionService(db), null!);
        var result = await service.PublishExamAsync(exam.Id, false, "owner", isAdmin: true);
        Assert.Equal(expected, result.Published);
        Assert.Equal(expected ? ExamPublishStatus.Published : ExamPublishStatus.Draft, exam.PublishStatus);
    }

    internal static OpenQuestion PairedQuestion() => new()
    {
        Id = 42,
        QuestionNumber = 1,
        Text = "Complete both blanks.",
        Score = 1,
        OpenAnswerFields =
        [
            new() { Key = "first", Label = "First missing word", AcceptedAnswers = [new() { Text = "om" }] },
            new() { Key = "second", Label = "Second missing word", AcceptedAnswers = [new() { Text = "te" }] }
        ]
    };

    internal static string Submission(string first, string second)
        => JsonSerializer.Serialize(new { format = "open-answers-v1", answers = new { first, second } });
}
