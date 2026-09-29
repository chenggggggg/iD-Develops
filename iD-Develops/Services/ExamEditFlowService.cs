using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Pages.Shared.Examination;
using iD_Develops.Pages.Shared.Examination.Questions;

namespace iD_Develops.Services
{
    public sealed class ExamEditFlowService : IExamEditFlowService
    {
        private readonly IExamService _examService;
        private readonly IQuestionService _questionService;
        private readonly IConfiguration _configuration;

        public ExamEditFlowService(
            IExamService examService,
            IQuestionService questionService,
            IConfiguration configuration)
        {
            _examService = examService;
            _questionService = questionService;
            _configuration = configuration;
        }

        public async Task<ExamEditorPageData?> LoadPageAsync(int examId, int questionId, string? userId, bool isAdmin)
        {
            if (examId <= 0)
                return null;

            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return null;

            var headerData = await _examService.GetExamHeaderAsync(examId);
            if (headerData == null)
                return null;

            var questionsMetadata = await _questionService.GetQuestionMetadataByExamIdAsync(examId);
            var exam = await _examService.GetExamForSettingsUpdateAsync(examId);
            var publishStatus = exam?.PublishStatus ?? ExamPublishStatus.Draft;

            var resolvedQuestionId = questionId;
            if (resolvedQuestionId == 0 && questionsMetadata.Any())
                resolvedQuestionId = questionsMetadata[0].QuestionId;

            var provider = (_configuration["Storage:Provider"] ?? "").Trim();
            var uploadsEnabled = !provider.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

            var pageData = new ExamEditorPageData
            {
                Header = new ExaminationHeaderModel
                {
                    ExamId = examId,
                    ExamTitle = headerData.Value.ExamTitle,
                    CourseName = headerData.Value.CourseName,
                    TimeLimit = headerData.Value.DurationSeconds,
                    Mode = PaginationMode.Editing
                },
                Pagination = new PaginationModel
                {
                    Mode = PaginationMode.Editing,
                    ExamId = examId,
                    QuestionsMetadata = questionsMetadata,
                    CurrentQuestionId = resolvedQuestionId,
                    PublishStatus = publishStatus
                },
                Settings = exam == null
                    ? new CreateExamInputModel()
                    : new CreateExamInputModel
                    {
                        Name = exam.Name,
                        TimeLimit = exam.TimeLimit,
                        DifficultyValue = exam.DifficultyValue,
                        MaxAttempts = exam.MaxAttempts == -1 ? 0 : exam.MaxAttempts,
                        PublicSlug = exam.PublicSlug,
                        IntroductionPrimaryLanguage = exam.IntroductionPrimaryLanguage,
                        IntroductionSecondaryLanguage = exam.IntroductionSecondaryLanguage,
                        CompletionTextPrimary = exam.CompletionTextPrimary,
                        CompletionTextSecondary = exam.CompletionTextSecondary,
                        ResultGradeDisplayMode = exam.ResultGradeDisplayMode,
                        ResultGradeCustomTextPrimary = exam.ResultGradeCustomTextPrimary,
                        ResultGradeCustomTextSecondary = exam.ResultGradeCustomTextSecondary,
                        ResultGradeBands = await BuildGradeBandInputsAsync(exam)
                    },
                Question = resolvedQuestionId > 0
                    ? await _questionService.GetQuestionForExamForEditAsync(examId, resolvedQuestionId)
                    : null,
                ResolvedQuestionId = resolvedQuestionId,
                RedirectToCanonicalQuestion = resolvedQuestionId > 0 && questionId == 0,
                UploadsEnabled = uploadsEnabled,
                StoragePrefix = _configuration["Storage:Prefix"] ?? "dev"
            };

            pageData.Settings.EnsureMinimumGradeBandRows();
            return pageData;
        }

        private async Task<List<ResultGradeBandInputModel>> BuildGradeBandInputsAsync(Exam exam)
        {
            var questions = await _questionService.GetQuestionsForExamForEditAsync(exam.Id);
            var maximumScore = questions
                .Select(q => q.Score.HasValue && q.Score.Value > 0 ? q.Score.Value : 1d)
                .DefaultIfEmpty(0d)
                .Sum();

            var sortedBands = exam.GradeBands
                .OrderBy(b => b.MinimumScore)
                .ToList();

            if (sortedBands.Count == 0)
                return new List<ResultGradeBandInputModel>();

            var inputs = new List<ResultGradeBandInputModel>(sortedBands.Count);
            for (var i = 0; i < sortedBands.Count; i++)
            {
                var nextMinimum = i + 1 < sortedBands.Count
                    ? sortedBands[i + 1].MinimumScore
                    : (double?)null;

                var upperScore = nextMinimum.HasValue
                    ? Math.Round(nextMinimum.Value - 0.1d, 1, MidpointRounding.AwayFromZero)
                    : Math.Round(Math.Max(maximumScore, sortedBands[i].MinimumScore), 1, MidpointRounding.AwayFromZero);

                inputs.Add(new ResultGradeBandInputModel
                {
                    MaximumScore = upperScore,
                    LabelPrimary = sortedBands[i].LabelPrimary
                });
            }

            return inputs;
        }

        public async Task<QuestionShellModel?> LoadQuestionShellAsync(int examId, int questionId, string? userId, bool isAdmin)
        {
            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return null;

            var question = await _questionService.GetQuestionForExamForEditAsync(examId, questionId);
            if (question == null)
                return null;

            return new QuestionShellModel
            {
                Question = question,
                Mode = QuestionRenderMode.Edit
            };
        }

        public QuestionShellModel BuildDraftQuestionShell(string kind, int questionNumber)
        {
            Question question = kind switch
            {
                "Open" => new OpenQuestion
                {
                    Id = 0,
                    Text = string.Empty,
                    QuestionNumber = questionNumber,
                    Score = 1,
                    CorrectAnswers = new List<CorrectAnswer>()
                },
                "MultipleChoice" => new MultipleChoiceQuestion
                {
                    Id = 0,
                    Text = string.Empty,
                    QuestionNumber = questionNumber,
                    Score = 1,
                    MultipleChoiceAnswer = new MultipleChoiceAnswer
                    {
                        AnswerA = string.Empty,
                        AnswerB = string.Empty,
                        AnswerC = string.Empty,
                        AnswerD = string.Empty
                    },
                    CorrectAnswers = new List<CorrectAnswer>()
                },
                "TrueOrFalse" => new TrueOrFalseQuestion
                {
                    Id = 0,
                    Text = string.Empty,
                    QuestionNumber = questionNumber,
                    Score = 1,
                    CorrectAnswers = new List<CorrectAnswer>()
                },
                _ => throw new InvalidOperationException("Unknown question type")
            };

            return new QuestionShellModel
            {
                Question = question,
                Mode = QuestionRenderMode.Edit
            };
        }

        public async Task<PaginationModel?> LoadPaginationAsync(int examId, int currentQuestionId, string? userId, bool isAdmin)
        {
            if (examId <= 0)
                return null;

            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return null;

            var questionsMetadata = await _questionService.GetQuestionMetadataByExamIdAsync(examId);
            if (currentQuestionId <= 0 || !questionsMetadata.Any(x => x.QuestionId == currentQuestionId))
                currentQuestionId = questionsMetadata.Any() ? questionsMetadata[0].QuestionId : 0;

            return new PaginationModel
            {
                Mode = PaginationMode.Editing,
                ExamId = examId,
                QuestionsMetadata = questionsMetadata,
                CurrentQuestionId = currentQuestionId
            };
        }

        private async Task<bool> CanManageExamAsync(int examId, string? userId, bool isAdmin)
        {
            if (isAdmin)
                return true;

            if (string.IsNullOrWhiteSpace(userId))
                return false;

            return await _examService.CanTeacherManageExamAsync(examId, userId);
        }
    }
}
