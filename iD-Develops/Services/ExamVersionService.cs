using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace iD_Develops.Services
{
    public sealed class ExamVersionService : IExamVersionService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly Dictionary<int, ExamVersion> _resolvedVersions = new();

        public ExamVersionService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<ExamVersion?> GetVersionAsync(int examId, int versionId) => GetResolvedVersionAsync(examId, versionId);

        public async Task<bool> IsMediaReferencedAsync(int examId, string reference)
        {
            var encodedReference = JsonSerializer.Serialize(reference);
            return await _dbContext.ExamVersionQuestions.AnyAsync(q => q.ExamVersion.ExamId == examId &&
                    (q.ImageReference == reference || q.AudioReference == reference)) ||
                await _dbContext.ExamVersions.AnyAsync(v => v.ExamId == examId && v.ChangesJson != null &&
                    v.ChangesJson.Contains(encodedReference));
        }

        public async Task<IReadOnlyList<ExamVersionSummary>> GetVersionHistoryAsync(IReadOnlyCollection<int> examIds)
        {
            var rows = await _dbContext.ExamVersions.AsNoTracking()
                .Where(v => examIds.Contains(v.ExamId))
                .OrderByDescending(v => v.PublishedAtUtc).ThenByDescending(v => v.VersionNumber)
                .Select(v => new { v.Id, v.ExamId, v.VersionNumber, v.PublishedAtUtc, Attempts = v.Records.Count(r => !r.IsDeleted) })
                .ToListAsync();
            var summaries = new List<ExamVersionSummary>();
            foreach (var row in rows)
            {
                var version = (await GetResolvedVersionAsync(row.ExamId, row.Id))!;
                summaries.Add(new(row.ExamId, row.Id, version.Name, row.VersionNumber, row.PublishedAtUtc,
                    version.TimeLimitSeconds, version.Questions.Count, row.Attempts));
            }
            return summaries;
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

            var version = examVersionId.HasValue ? await GetResolvedVersionAsync(examId, examVersionId) : null;
            if (version != null)
            {
                return new PublishedExamDescriptor(
                    version.ExamId,
                    version.Id,
                    version.Name,
                    version.CourseName ?? string.Empty,
                    version.TimeLimitSeconds ?? 0,
                    version.IntroductionPrimaryLanguage,
                    version.IntroductionSecondaryLanguage,
                    NormalizeMaxAttempts(version.MaxAttempts),
                    version.PublicSlug,
                    version.VersionNumber);
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
                    e.TimeLimitSeconds,
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
                exam.TimeLimitSeconds ?? 0,
                exam.IntroductionPrimaryLanguage,
                exam.IntroductionSecondaryLanguage,
                NormalizeMaxAttempts(exam.MaxAttempts),
                exam.PublicSlug);
        }

        public async Task<List<QuestionMetadata>> GetQuestionMetadataAsync(int examId, int? examVersionId = null)
        {
            var version = examVersionId.HasValue ? await GetResolvedVersionAsync(examId, examVersionId) : null;
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

            if (examVersionId.HasValue)
                return [];

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
            var version = examVersionId.HasValue ? await GetResolvedVersionAsync(examId, examVersionId) : null;
            if (version != null)
            {
                var versionQuestion = version.Questions
                    .FirstOrDefault(q => q.SourceQuestionId == questionId);

                return versionQuestion == null
                    ? null
                    : MapSnapshotQuestion(version.ExamId, versionQuestion, sanitizeForTake: true);
            }

            if (examVersionId.HasValue)
                return null;

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
            var version = examVersionId.HasValue ? await GetResolvedVersionAsync(examId, examVersionId) : null;
            if (version != null)
            {
                return new Exam
                {
                    Id = version.ExamId,
                    Name = version.Name,
                    DifficultyValue = version.DifficultyValue,
                    TimeLimitSeconds = version.TimeLimitSeconds,
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

            if (examVersionId.HasValue)
                return null;

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

        public async Task<PublishedExamDescriptor?> GetOrCreateAttemptVersionAsync(int examId)
        {
            return await InSnapshotTransactionAsync<PublishedExamDescriptor?>(async () =>
            {
                var exam = await _dbContext.Exams.AsNoTracking()
                    .Include(e => e.Course).Include(e => e.GradeBands)
                    .FirstOrDefaultAsync(e => e.Id == examId && !e.IsDeleted && e.PublishStatus == ExamPublishStatus.Published);
                if (exam == null)
                    return null;
                var questions = await BuildLiveQuestionQuery(false)
                    .Where(q => q.ExamId == examId && !q.IsDeleted).ToListAsync();
                var version = await PreserveSnapshotAsync(exam, questions);
                return await GetPublishedDescriptorAsync(examId, version.Id);
            });
        }

        private async Task<T> InSnapshotTransactionAsync<T>(Func<Task<T>> action)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                try
                {
                    var result = await action();
                    await transaction.CommitAsync();
                    return result;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    // A retry must not reinsert an entity from the rolled-back attempt.
                    foreach (var entry in _dbContext.ChangeTracker.Entries<ExamVersion>().ToList())
                        entry.State = EntityState.Detached;
                    _resolvedVersions.Clear();
                    throw;
                }
            });
        }

        private async Task<ExamVersion> PreserveSnapshotAsync(Exam exam, IReadOnlyCollection<Question> questions)
        {
            var previous = await GetResolvedVersionAsync(exam.Id, null);
            var snapshot = new ExamVersion
            {
                Name = exam.Name,
                DifficultyValue = exam.DifficultyValue,
                TimeLimitSeconds = exam.TimeLimitSeconds,
                IntroductionPrimaryLanguage = exam.IntroductionPrimaryLanguage,
                IntroductionSecondaryLanguage = exam.IntroductionSecondaryLanguage,
                CompletionTextPrimary = exam.CompletionTextPrimary,
                CompletionTextSecondary = exam.CompletionTextSecondary,
                ResultGradeDisplayMode = exam.ResultGradeDisplayMode,
                ResultGradeCustomTextPrimary = exam.ResultGradeCustomTextPrimary,
                ResultGradeCustomTextSecondary = exam.ResultGradeCustomTextSecondary,
                MaxAttempts = exam.MaxAttempts,
                PublicSlug = exam.PublicSlug,
                CourseName = exam.Course?.Name,
                GradeBands = exam.GradeBands.Select(b => new ExamVersionGradeBand
                {
                    MinimumScore = b.MinimumScore,
                    LabelPrimary = b.LabelPrimary,
                    LabelSecondary = b.LabelSecondary
                }).ToList(),
                Questions = questions.OrderBy(q => q.QuestionNumber).Select(CreateSnapshotQuestion).ToList()
            };
            var changes = ExamSnapshotDelta.Compare(previous == null ? null : ExamSnapshotDelta.Encode(previous), ExamSnapshotDelta.Encode(snapshot));
            if (changes.Count == 0)
                return previous!;

            // Keep old full snapshots readable. New rows contain only changes;
            // unchanged questions, answers and settings come from the parent.
            var version = new ExamVersion
            {
                ExamId = exam.Id,
                VersionNumber = (previous?.VersionNumber ?? 0) + 1,
                ParentVersionId = previous?.Id,
                PublishedAtUtc = DateTime.UtcNow,
                ChangesJson = JsonSerializer.Serialize(changes)
            };
            _dbContext.ExamVersions.Add(version);
            await _dbContext.SaveChangesAsync();
            return version;
        }

        private async Task<ExamVersion?> GetResolvedVersionAsync(int examId, int? examVersionId)
        {
            if (examId <= 0)
                return null;
            if (examVersionId.HasValue && _resolvedVersions.TryGetValue(examVersionId.Value, out var cached))
                return cached.ExamId == examId ? cached : null;
            if (examVersionId is <= 0)
                return null;
            var query = _dbContext.ExamVersions.AsNoTracking().Where(v => v.ExamId == examId);
            var target = examVersionId.HasValue
                ? await query.FirstOrDefaultAsync(v => v.Id == examVersionId.Value)
                : await query.OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync();
            if (target == null)
                return null;

            // Load the small delta rows together rather than making one round trip
            // per version. Relational question payloads are loaded only for a legacy root.
            var rows = await query.Where(v => v.VersionNumber <= target.VersionNumber).ToDictionaryAsync(v => v.Id);
            var ancestors = new Stack<ExamVersion>();
            var cursor = target;
            var visited = new HashSet<int>();
            JsonObject? document = null;
            while (true)
            {
                if (!visited.Add(cursor.Id))
                    throw new InvalidOperationException("Exam version ancestry contains a cycle.");
                if (_resolvedVersions.TryGetValue(cursor.Id, out var preserved))
                {
                    document = ExamSnapshotDelta.Encode(preserved);
                    break;
                }
                if (cursor.ChangesJson == null)
                {
                    cursor = await query.AsSplitQuery().Include(v => v.Questions).ThenInclude(q => q.CorrectAnswers)
                        .Include(v => v.GradeBands).SingleAsync(v => v.Id == cursor.Id);
                }
                ancestors.Push(cursor);
                if (!cursor.ParentVersionId.HasValue)
                    break;
                cursor = rows.GetValueOrDefault(cursor.ParentVersionId.Value)
                    ?? throw new InvalidOperationException("Exam version parent is missing.");
            }
            foreach (var version in ancestors)
            {
                document = version.ChangesJson == null
                    ? ExamSnapshotDelta.Encode(version)
                    : ExamSnapshotDelta.Apply(document, version.ChangesJson);
                var resolved = ExamSnapshotDelta.Decode(document!);
                resolved.Id = version.Id;
                resolved.ExamId = version.ExamId;
                resolved.VersionNumber = version.VersionNumber;
                resolved.PublishedAtUtc = version.PublishedAtUtc;
                _resolvedVersions[version.Id] = resolved;
            }
            return _resolvedVersions[target.Id];
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
