using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace iD_Develops.Services
{
    public sealed class ExamVersionService : IExamVersionService
    {
        private readonly ApplicationDbContext _dbContext;

        public ExamVersionService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<PublishedExamDescriptor?> GetPublishedDescriptorAsync(int examId, int? examVersionId = null)
        {
            if (examId <= 0)
                return null;

            if (!examVersionId.HasValue || examVersionId.Value <= 0)
            {
                var isPublished = await _dbContext.Exams
                    .AsNoTracking()
                    .AnyAsync(e =>
                        e.Id == examId &&
                        !e.IsDeleted &&
                        e.PublishStatus == ExamPublishStatus.Published);

                if (!isPublished)
                    return null;
            }

            var version = await GetResolvedVersionAsync(examId, examVersionId);
            if (version != null)
            {
                return new PublishedExamDescriptor(
                    version.ExamId,
                    version.Id,
                    version.Name,
                    version.CourseName ?? string.Empty,
                    version.TimeLimit.HasValue ? version.TimeLimit.Value * 60 : 0,
                    version.IntroductionPrimaryLanguage,
                    version.IntroductionSecondaryLanguage,
                    NormalizeMaxAttempts(version.MaxAttempts),
                    version.TimeLimit,
                    version.PublicSlug);
            }

            if (examVersionId.HasValue && examVersionId.Value > 0)
                return null;

            var exam = await _dbContext.Exams
                .AsNoTracking()
                .Where(e => e.Id == examId && !e.IsDeleted && e.PublishStatus == ExamPublishStatus.Published)
                .Select(e => new
                {
                    e.Id,
                    e.Name,
                    CourseName = e.Course != null ? e.Course.Name : string.Empty,
                    e.TimeLimit,
                    e.IntroductionPrimaryLanguage,
                    e.IntroductionSecondaryLanguage,
                    e.MaxAttempts,
                    e.PublicSlug
                })
                .FirstOrDefaultAsync();

            if (exam == null)
                return null;

            return new PublishedExamDescriptor(
                exam.Id,
                null,
                exam.Name,
                exam.CourseName,
                exam.TimeLimit.HasValue ? exam.TimeLimit.Value * 60 : 0,
                exam.IntroductionPrimaryLanguage,
                exam.IntroductionSecondaryLanguage,
                NormalizeMaxAttempts(exam.MaxAttempts),
                exam.TimeLimit,
                exam.PublicSlug);
        }

        public async Task<List<QuestionMetadata>> GetQuestionMetadataAsync(int examId, int? examVersionId = null)
        {
            var version = await GetResolvedVersionAsync(examId, examVersionId);
            if (version != null)
            {
                return version.Questions
                    .OrderBy(q => q.QuestionNumber)
                    .Select(q => new QuestionMetadata
                    {
                        QuestionId = q.SourceQuestionId,
                        QuestionNumber = q.QuestionNumber
                    })
                    .ToList();
            }

            return await _dbContext.Questions
                .AsNoTracking()
                .Where(q => q.ExamId == examId && !q.IsDeleted)
                .OrderBy(q => q.QuestionNumber)
                .Select(q => new QuestionMetadata
                {
                    QuestionId = q.Id,
                    QuestionNumber = q.QuestionNumber
                })
                .ToListAsync();
        }

        public async Task<Question?> GetQuestionForTakeAsync(int examId, int questionId, int? examVersionId = null)
        {
            var version = await GetResolvedVersionAsync(examId, examVersionId);
            if (version != null)
            {
                var versionQuestion = version.Questions
                    .FirstOrDefault(q => q.SourceQuestionId == questionId);

                return versionQuestion == null
                    ? null
                    : MapSnapshotQuestion(version.ExamId, versionQuestion, sanitizeForTake: true);
            }

            var liveQuestion = await BuildLiveQuestionQuery(trackChanges: false)
                .FirstOrDefaultAsync(q => q.Id == questionId && q.ExamId == examId && !q.IsDeleted);

            if (liveQuestion == null)
                return null;

            liveQuestion.Feedback = string.Empty;
            liveQuestion.FunFact = string.Empty;
            liveQuestion.CorrectAnswers = new List<CorrectAnswer>();
            return liveQuestion;
        }

        public async Task<Exam?> GetExamForEvaluationAsync(int examId, int? examVersionId = null)
        {
            var version = await GetResolvedVersionAsync(examId, examVersionId);
            if (version != null)
            {
                return new Exam
                {
                    Id = version.ExamId,
                    Name = version.Name,
                    DifficultyValue = version.DifficultyValue,
                    TimeLimit = version.TimeLimit,
                    IntroductionPrimaryLanguage = version.IntroductionPrimaryLanguage,
                    IntroductionSecondaryLanguage = version.IntroductionSecondaryLanguage,
                    CompletionTextPrimary = version.CompletionTextPrimary,
                    CompletionTextSecondary = version.CompletionTextSecondary,
                    ResultGradeDisplayMode = version.ResultGradeDisplayMode,
                    ResultGradeCustomTextPrimary = version.ResultGradeCustomTextPrimary,
                    ResultGradeCustomTextSecondary = version.ResultGradeCustomTextSecondary,
                    GradeBands = version.GradeBands
                        .OrderByDescending(b => b.MinimumScore)
                        .Select(b => new ExamGradeBand
                        {
                            MinimumScore = b.MinimumScore,
                            LabelPrimary = b.LabelPrimary,
                            LabelSecondary = b.LabelSecondary
                        })
                        .ToList(),
                    MaxAttempts = version.MaxAttempts,
                    PublicSlug = version.PublicSlug,
                    Questions = version.Questions
                        .OrderBy(q => q.QuestionNumber)
                        .Select(q => MapSnapshotQuestion(version.ExamId, q, sanitizeForTake: false))
                        .ToList()
                };
            }

            var exam = await _dbContext.Exams
                .AsNoTracking()
                .Include(e => e.GradeBands)
                .FirstOrDefaultAsync(e => e.Id == examId && !e.IsDeleted);

            if (exam == null)
                return null;

            exam.Questions = await BuildLiveQuestionQuery(trackChanges: false)
                .Where(q => q.ExamId == examId && !q.IsDeleted)
                .OrderBy(q => q.QuestionNumber)
                .ToListAsync();

            return exam;
        }

        public async Task<OperationResult> PublishVersionAsync(Exam exam, IReadOnlyCollection<Question> questions)
        {
            if (exam == null)
                return new OperationResult { Success = false, ErrorMessage = "Exam not found." };

            var strategy = _dbContext.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var nextVersionNumber = await _dbContext.ExamVersions
                    .Where(v => v.ExamId == exam.Id)
                    .Select(v => (int?)v.VersionNumber)
                    .MaxAsync() ?? 0;

                var version = new ExamVersion
                {
                    ExamId = exam.Id,
                    VersionNumber = nextVersionNumber + 1,
                    Name = exam.Name,
                    DifficultyValue = exam.DifficultyValue,
                    TimeLimit = exam.TimeLimit,
                    IntroductionPrimaryLanguage = exam.IntroductionPrimaryLanguage,
                    IntroductionSecondaryLanguage = exam.IntroductionSecondaryLanguage,
                    CompletionTextPrimary = exam.CompletionTextPrimary,
                    CompletionTextSecondary = exam.CompletionTextSecondary,
                    ResultGradeDisplayMode = exam.ResultGradeDisplayMode,
                    ResultGradeCustomTextPrimary = exam.ResultGradeCustomTextPrimary,
                    ResultGradeCustomTextSecondary = exam.ResultGradeCustomTextSecondary,
                    GradeBands = exam.GradeBands
                        .OrderByDescending(b => b.MinimumScore)
                        .Select(b => new ExamVersionGradeBand
                        {
                            MinimumScore = b.MinimumScore,
                            LabelPrimary = b.LabelPrimary,
                            LabelSecondary = b.LabelSecondary
                        })
                        .ToList(),
                    MaxAttempts = exam.MaxAttempts,
                    PublicSlug = exam.PublicSlug,
                    CourseName = exam.Course?.Name,
                    PublishedAtUtc = DateTime.UtcNow,
                    Questions = questions
                        .OrderBy(q => q.QuestionNumber)
                        .Select(CreateSnapshotQuestion)
                        .ToList()
                };

                _dbContext.ExamVersions.Add(version);
                exam.PublishStatus = ExamPublishStatus.Published;
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            return new OperationResult { Success = true };
        }

        private async Task<ExamVersion?> GetResolvedVersionAsync(int examId, int? examVersionId)
        {
            if (examId <= 0)
                return null;

            var query = _dbContext.ExamVersions
                .AsNoTracking()
                .Include(v => v.Questions)
                    .ThenInclude(q => q.CorrectAnswers)
                .Include(v => v.GradeBands)
                .Where(v => v.ExamId == examId);

            if (examVersionId.HasValue && examVersionId.Value > 0)
            {
                return await query.FirstOrDefaultAsync(v => v.Id == examVersionId.Value);
            }

            return await query
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();
        }

        private IQueryable<Question> BuildLiveQuestionQuery(bool trackChanges)
        {
            IQueryable<Question> query = _dbContext.Questions;
            if (!trackChanges)
                query = query.AsNoTracking();

            return query
                .Include(q => q.CorrectAnswers)
                .Include("MultipleChoiceAnswer");
        }

        private static int NormalizeMaxAttempts(int maxAttempts)
        {
            if (maxAttempts < 1)
                return -1;

            return maxAttempts;
        }

        private static ExamVersionQuestion CreateSnapshotQuestion(Question question)
        {
            return new ExamVersionQuestion
            {
                SourceQuestionId = question.Id,
                QuestionType = question switch
                {
                    MultipleChoiceQuestion => "MultipleChoice",
                    TrueOrFalseQuestion => "TrueOrFalse",
                    OpenQuestion => "Open",
                    _ => "Unknown"
                },
                QuestionNumber = question.QuestionNumber,
                MessageBeforeQuestion = question.MessageBeforeQuestion,
                ImageReference = question.ImageReference,
                AudioReference = question.AudioReference,
                Text = question.Text,
                Scenario = question.Scenario,
                Feedback = question.Feedback,
                FunFact = question.FunFact,
                Score = question.Score,
                AnswerA = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerA,
                AnswerB = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerB,
                AnswerC = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerC,
                AnswerD = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerD,
                CorrectAnswers = question.CorrectAnswers
                    .Where(a => !string.IsNullOrWhiteSpace(a.Text))
                    .Select(a => new ExamVersionCorrectAnswer
                    {
                        Text = a.Text,
                        Score = a.Score
                    })
                    .ToList()
            };
        }

        private static Question MapSnapshotQuestion(int examId, ExamVersionQuestion snapshot, bool sanitizeForTake)
        {
            var feedback = sanitizeForTake ? string.Empty : snapshot.Feedback;
            var funFact = sanitizeForTake ? string.Empty : snapshot.FunFact;
            var correctAnswers = sanitizeForTake
                ? new List<CorrectAnswer>()
                : snapshot.CorrectAnswers
                    .Select(a => new CorrectAnswer
                    {
                        Id = a.Id,
                        QuestionId = snapshot.SourceQuestionId,
                        Text = a.Text,
                        Score = a.Score
                    })
                    .ToList();

            return snapshot.QuestionType switch
            {
                "MultipleChoice" => new MultipleChoiceQuestion
                {
                    Id = snapshot.SourceQuestionId,
                    ExamId = examId,
                    QuestionNumber = snapshot.QuestionNumber,
                    MessageBeforeQuestion = snapshot.MessageBeforeQuestion,
                    ImageReference = snapshot.ImageReference,
                    AudioReference = snapshot.AudioReference,
                    Text = snapshot.Text,
                    Scenario = snapshot.Scenario,
                    Feedback = feedback,
                    FunFact = funFact,
                    Score = snapshot.Score,
                    CorrectAnswers = correctAnswers,
                    MultipleChoiceAnswer = new MultipleChoiceAnswer
                    {
                        QuestionId = snapshot.SourceQuestionId,
                        AnswerA = snapshot.AnswerA,
                        AnswerB = snapshot.AnswerB,
                        AnswerC = snapshot.AnswerC,
                        AnswerD = snapshot.AnswerD
                    }
                },
                "TrueOrFalse" => new TrueOrFalseQuestion
                {
                    Id = snapshot.SourceQuestionId,
                    ExamId = examId,
                    QuestionNumber = snapshot.QuestionNumber,
                    MessageBeforeQuestion = snapshot.MessageBeforeQuestion,
                    ImageReference = snapshot.ImageReference,
                    AudioReference = snapshot.AudioReference,
                    Text = snapshot.Text,
                    Scenario = snapshot.Scenario,
                    Feedback = feedback,
                    FunFact = funFact,
                    Score = snapshot.Score,
                    CorrectAnswers = correctAnswers
                },
                _ => new OpenQuestion
                {
                    Id = snapshot.SourceQuestionId,
                    ExamId = examId,
                    QuestionNumber = snapshot.QuestionNumber,
                    MessageBeforeQuestion = snapshot.MessageBeforeQuestion,
                    ImageReference = snapshot.ImageReference,
                    AudioReference = snapshot.AudioReference,
                    Text = snapshot.Text,
                    Scenario = snapshot.Scenario,
                    Feedback = feedback,
                    FunFact = funFact,
                    Score = snapshot.Score,
                    CorrectAnswers = correctAnswers
                }
            };
        }
    }
}
