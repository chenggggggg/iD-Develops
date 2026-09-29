using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Pages.Shared.Examination;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.Extensions.Configuration;

namespace iD_Develops.Tests.Services;

public sealed class ExamEditFlowServiceTests
{
    [Fact]
    public async Task LoadPageAsync_RebuildsUpperScoreBandsFromQuestionScores_WhenExamQuestionsAreNotLoaded()
    {
        var exam = new Exam
        {
            Id = 42,
            Name = "Dutch placement",
            CreatedByUserId = "teacher-1",
            DifficultyValue = (int)DifficultyLevel.A1,
            IntroductionPrimaryLanguage = "Intro",
            ResultGradeDisplayMode = ResultGradeDisplayMode.GradeBands,
            GradeBands =
            [
                new ExamGradeBand { MinimumScore = 4.1d, LabelPrimary = "Pass" },
                new ExamGradeBand { MinimumScore = 0d, LabelPrimary = "Needs work" }
            ],
            Questions = new List<Question>()
        };

        var examService = new StubExamService(exam);
        var questionService = new StubQuestionService(
            questions:
            [
                new OpenQuestion { Id = 100, ExamId = 42, QuestionNumber = 1, Text = "Q1", Score = 2.5d },
                new OpenQuestion { Id = 101, ExamId = 42, QuestionNumber = 2, Text = "Q2", Score = 2.5d }
            ]);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "Disabled",
                ["Storage:Prefix"] = "dev"
            })
            .Build();

        var service = new ExamEditFlowService(examService, questionService, configuration);

        var pageData = await service.LoadPageAsync(42, 0, "teacher-1", isAdmin: false);

        Assert.NotNull(pageData);
        Assert.Equal(ResultGradeDisplayMode.GradeBands, pageData!.Settings.ResultGradeDisplayMode);

        var gradeBands = pageData.Settings.ResultGradeBands.ToList();
        Assert.Collection(
            gradeBands,
            band =>
            {
                Assert.NotNull(band.MaximumScore);
                Assert.Equal(4.0d, band.MaximumScore.Value, precision: 1);
                Assert.Equal("Needs work", band.LabelPrimary);
            },
            band =>
            {
                Assert.NotNull(band.MaximumScore);
                Assert.Equal(5.0d, band.MaximumScore.Value, precision: 1);
                Assert.Equal("Pass", band.LabelPrimary);
            });
    }

    private sealed class StubExamService : IExamService
    {
        private readonly Exam _exam;

        public StubExamService(Exam exam)
        {
            _exam = exam;
        }

        public Task<bool> CanTeacherManageExamAsync(int examId, string teacherUserId)
            => Task.FromResult(examId == _exam.Id && teacherUserId == _exam.CreatedByUserId);

        public Task<OperationResult> CreateExamAsync(Exam exam)
            => throw new NotSupportedException();

        public Task<OperationResult> DeleteExamByIdAsync(int examId)
            => throw new NotSupportedException();

        public Task<OperationResult> ArchiveExamAsync(int examId)
            => throw new NotSupportedException();

        public Task<List<Exam>> GetAllExamsAsync()
            => throw new NotSupportedException();

        public Task<(string ExamTitle, string CourseName, int DurationSeconds)?> GetExamHeaderAsync(int examId)
            => Task.FromResult<(string ExamTitle, string CourseName, int DurationSeconds)?>((_exam.Name, string.Empty, 0));

        public Task<Exam?> GetExamForSettingsUpdateAsync(int examId)
            => Task.FromResult<Exam?>(examId == _exam.Id ? _exam : null);

        public Task<(bool Exists, int MaxAttempts, int? TimeLimit)> GetExamStartSettingsAsync(int examId)
            => throw new NotSupportedException();

        public Task<List<Exam>> GetExamsByTeacherAsync(string teacherUserId)
            => throw new NotSupportedException();

        public Task<Exam?> GetPublicExamBySlugAsync(string publicSlug)
            => throw new NotSupportedException();

        public Task<List<Exam>> GetPublishedExamsAsync()
            => throw new NotSupportedException();

        public Task<List<Exam>> GetPublishedExamsForUserAsync(string userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveChangesAsync()
            => throw new NotSupportedException();
    }

    private sealed class StubQuestionService : IQuestionService
    {
        private readonly List<Question> _questions;

        public StubQuestionService(List<Question> questions)
        {
            _questions = questions;
        }

        public Task<Question?> GetQuestionByIdWithoutSensitiveInfoAsync(int examId, int questionId)
            => throw new NotSupportedException();

        public Task<List<QuestionMetadata>> GetQuestionMetadataByExamIdAsync(int examId)
            => Task.FromResult(new List<QuestionMetadata>());

        public Task<List<Question>> GetQuestionsForExamForEditAsync(int examId)
            => Task.FromResult(_questions.Where(q => q.ExamId == examId).ToList());

        public Task<Question?> GetQuestionByNumberForUpdateAsync(int examId, int questionNumber)
            => Task.FromResult(_questions.FirstOrDefault(q => q.ExamId == examId && q.QuestionNumber == questionNumber));

        public Task AddQuestionAsync(int examId, Question question)
            => throw new NotSupportedException();

        public Task<Question?> GetQuestionForExamForUpdateAsync(int examId, int questionId)
            => throw new NotSupportedException();

        public Task<Question?> GetQuestionForExamForEditAsync(int examId, int questionId)
            => Task.FromResult<Question?>(null);

        public Task UpdateQuestionAsync(Question question)
            => throw new NotSupportedException();

        public Task ApplyAutosavePatchAsync(
            Question trackedQuestion,
            string? text,
            string? messageBeforeQuestion,
            string? scenario,
            string? feedback,
            string? funFact,
            double? score,
            string? openCorrectAnswerText,
            string? multipleChoiceAnswerA,
            string? multipleChoiceAnswerB,
            string? multipleChoiceAnswerC,
            string? multipleChoiceAnswerD,
            string? multipleChoiceCorrect,
            string? trueOrFalseCorrect,
            string? imageReference,
            string? audioReference)
            => throw new NotSupportedException();

        public Task DeleteQuestionFromExamAsync(int examId, int questionId)
            => throw new NotSupportedException();
    }
}
