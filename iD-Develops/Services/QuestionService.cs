using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public class QuestionService : IQuestionService
    {
        private readonly ApplicationDbContext _dbContext;

        public QuestionService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Returns a question for exam-take mode, scoped by exam and sanitized
        /// (Feedback/FunFact + any correct answers are removed).
        /// Uses AsNoTracking to avoid accidentally persisting sanitization changes.
        /// </summary>
        public async Task<Question?> GetQuestionByIdWithoutSensitiveInfoAsync(int examId, int questionId)
        {
            if (examId <= 0 || questionId <= 0) return null;

            var q = await BuildQuestionQuery(trackChanges: false)
                .FirstOrDefaultAsync(q => q.Id == questionId && q.ExamId == examId && !q.IsDeleted);

            if (q == null) return null;

            // Sanitize
            q.Feedback = string.Empty;
            q.FunFact = string.Empty;

            // Ensure no correct answers leak out
            q.CorrectAnswers = new List<CorrectAnswer>();

            return q;
        }

        public async Task<List<QuestionMetadata>> GetQuestionMetadataByExamIdAsync(int examId)
        {
            return await _dbContext.Questions
                .AsNoTracking()
                .Where(q => q.ExamId == examId && !q.IsDeleted)
                .OrderBy(q => q.QuestionNumber)
                .Select(q => new QuestionMetadata
                {
                    QuestionNumber = q.QuestionNumber,
                    QuestionId = q.Id
                })
                .ToListAsync();
        }

        public async Task<List<Question>> GetQuestionsForExamForEditAsync(int examId)
        {
            var openQuestions = await _dbContext.OpenQuestions
                .AsNoTracking()
                .Include(q => q.CorrectAnswers)
                .Where(q => q.ExamId == examId && !q.IsDeleted)
                .ToListAsync();

            var trueOrFalseQuestions = await _dbContext.TrueOrFalseQuestions
                .AsNoTracking()
                .Include(q => q.CorrectAnswers)
                .Where(q => q.ExamId == examId && !q.IsDeleted)
                .ToListAsync();

            var multipleChoiceQuestions = await _dbContext.MultipleChoiceQuestions
                .AsNoTracking()
                .Include(q => q.MultipleChoiceAnswer)
                .Include(q => q.CorrectAnswers)
                .Where(q => q.ExamId == examId && !q.IsDeleted)
                .ToListAsync();

            return openQuestions
                .Cast<Question>()
                .Concat(trueOrFalseQuestions)
                .Concat(multipleChoiceQuestions)
                .OrderBy(q => q.QuestionNumber)
                .ToList();
        }

        public async Task<Question?> GetQuestionByNumberForUpdateAsync(int examId, int questionNumber)
        {
            if (examId <= 0 || questionNumber <= 0) return null;

            return await BuildQuestionQuery(trackChanges: true)
                .FirstOrDefaultAsync(q =>
                    q.ExamId == examId
                    && q.QuestionNumber == questionNumber
                    && !q.IsDeleted);
        }

        public async Task AddQuestionAsync(int examId, Question question)
        {
            if (examId <= 0) throw new ArgumentOutOfRangeException(nameof(examId));
            if (question is null) throw new ArgumentNullException(nameof(question));

            var examExists = await _dbContext.Exams.AnyAsync(e => e.Id == examId && !e.IsDeleted);
            if (!examExists)
                throw new InvalidOperationException("Exam not found.");

            question.ExamId = examId;

            _dbContext.Questions.Add(question);
            await _dbContext.SaveChangesAsync();
        }

        // Tracked version for updates
        public async Task<Question?> GetQuestionForExamForUpdateAsync(int examId, int questionId)
        {
            if (examId <= 0 || questionId <= 0) return null;

            return await BuildQuestionQuery(trackChanges: true)
                .FirstOrDefaultAsync(q => q.Id == questionId && q.ExamId == examId && !q.IsDeleted);
        }

        // NoTracking read for EDITING (safe to expose correct answers because only editors call it)
        public async Task<Question?> GetQuestionForExamForEditAsync(int examId, int questionId)
        {
            if (examId <= 0 || questionId <= 0) return null;

            return await BuildQuestionQuery(trackChanges: false)
                .FirstOrDefaultAsync(q => q.Id == questionId && q.ExamId == examId && !q.IsDeleted);
        }

        public async Task UpdateQuestionAsync(Question question)
        {
            if (question == null) throw new ArgumentNullException(nameof(question));
            await _dbContext.SaveChangesAsync();
        }

        public async Task ApplyAutosavePatchAsync(
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
        {
            if (trackedQuestion == null) throw new ArgumentNullException(nameof(trackedQuestion));

            // Base fields
            trackedQuestion.Text = text ?? "";
            trackedQuestion.MessageBeforeQuestion = messageBeforeQuestion;
            trackedQuestion.Scenario = scenario;
            trackedQuestion.Feedback = feedback;
            trackedQuestion.FunFact = funFact;
            trackedQuestion.Score = NormalizeQuestionScore(score);
            trackedQuestion.ImageReference = imageReference;
            trackedQuestion.AudioReference = audioReference;

            // Type-specific
            if (trackedQuestion is OpenQuestion)
            {
                var value = openCorrectAnswerText?.Trim();

                if (string.IsNullOrWhiteSpace(value))
                {
                    await SoftDeleteAllCorrectAnswersAsync(trackedQuestion.Id);
                }
                else
                {
                    await UpsertSingleCorrectAnswerAsync(trackedQuestion.Id, value, trackedQuestion.Score);
                }
            }
            else if (trackedQuestion is MultipleChoiceQuestion mcq)
            {
                mcq.MultipleChoiceAnswer ??= new MultipleChoiceAnswer { QuestionId = trackedQuestion.Id };

                // Draft-friendly: all can be null until publish validation.
                mcq.MultipleChoiceAnswer.AnswerA = string.IsNullOrWhiteSpace(multipleChoiceAnswerA) ? null : multipleChoiceAnswerA.Trim();
                mcq.MultipleChoiceAnswer.AnswerB = string.IsNullOrWhiteSpace(multipleChoiceAnswerB) ? null : multipleChoiceAnswerB.Trim();
                mcq.MultipleChoiceAnswer.AnswerC = string.IsNullOrWhiteSpace(multipleChoiceAnswerC) ? null : multipleChoiceAnswerC.Trim();
                mcq.MultipleChoiceAnswer.AnswerD = string.IsNullOrWhiteSpace(multipleChoiceAnswerD) ? null : multipleChoiceAnswerD.Trim();                // Correct is optional in draft.
                // Keep selected letter even if some option texts are empty;
                // publish validation will warn about incomplete options.
                string? letter = string.IsNullOrWhiteSpace(multipleChoiceCorrect)
                    ? null
                    : multipleChoiceCorrect.Trim().ToUpperInvariant();

                if (letter is not (null or "A" or "B" or "C" or "D"))
                    letter = null;

                if (letter is null)
                {
                    await SoftDeleteAllCorrectAnswersAsync(trackedQuestion.Id);
                }
                else
                {
                    await UpsertSingleCorrectAnswerAsync(trackedQuestion.Id, letter, trackedQuestion.Score);
                }
            }
            else if (trackedQuestion is TrueOrFalseQuestion)
            {
                // Draft-friendly: optional; publish validation can enforce if you want.
                if (string.IsNullOrWhiteSpace(trueOrFalseCorrect))
                {
                    await SoftDeleteAllCorrectAnswersAsync(trackedQuestion.Id);
                }
                else
                {
                    var normalized = trueOrFalseCorrect.Trim().Equals("False", StringComparison.OrdinalIgnoreCase)
                        ? "False"
                        : "True";

                    await UpsertSingleCorrectAnswerAsync(trackedQuestion.Id, normalized, trackedQuestion.Score);
                }
            }
        }

        public async Task DeleteQuestionFromExamAsync(int examId, int questionId)
        {
            var q = await _dbContext.Questions
                .FirstOrDefaultAsync(x => x.Id == questionId && x.ExamId == examId);

            if (q == null)
                throw new InvalidOperationException("Question not found for this exam.");

            if (q.IsDeleted)
                return;

            var deletedNumber = q.QuestionNumber;

            q.IsDeleted = true;

            var toShift = await _dbContext.Questions
                .Where(x => x.ExamId == examId
                            && !x.IsDeleted
                            && x.QuestionNumber > deletedNumber)
                .ToListAsync();

            foreach (var item in toShift)
                item.QuestionNumber -= 1;
            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// Soft-deletes all correct answers for a question (draft state / "unset correct answer").
        /// </summary>
        private async Task SoftDeleteAllCorrectAnswersAsync(int questionId)
        {
            // If you added a global query filter on CorrectAnswer, IgnoreQueryFilters lets us find already-deleted rows too.
            var answers = await _dbContext.CorrectAnswers
                .IgnoreQueryFilters()
                .Where(ca => ca.QuestionId == questionId && !ca.IsDeleted)
                .ToListAsync();

            foreach (var ca in answers)
                ca.IsDeleted = true;
        }

        /// <summary>
        /// Ensures exactly one non-deleted correct answer row exists for the question.
        /// Reuses the oldest row if present, otherwise inserts a new one.
        /// </summary>
        private async Task UpsertSingleCorrectAnswerAsync(int questionId, string value, double? score)
        {
            var answers = await _dbContext.CorrectAnswers
                .IgnoreQueryFilters()
                .Where(ca => ca.QuestionId == questionId)
                .OrderBy(ca => ca.Id)
                .ToListAsync();

            // Soft-delete all existing (so we guarantee "single")
            foreach (var ca in answers)
                ca.IsDeleted = true;

            var first = answers.FirstOrDefault();
            if (first == null)
            {
                _dbContext.CorrectAnswers.Add(new CorrectAnswer
                {
                    QuestionId = questionId,
                    Text = value,
                    Score = NormalizeQuestionScore(score),
                    IsDeleted = false
                });
            }
            else
            {
                first.Text = value;
                first.Score = NormalizeQuestionScore(score);
                first.IsDeleted = false;
            }
        }

        private static double NormalizeQuestionScore(double? score)
            => score.HasValue && score.Value > 0 ? score.Value : 1d;

        private IQueryable<Question> BuildQuestionQuery(bool trackChanges)
        {
            IQueryable<Question> query = _dbContext.Questions;

            if (!trackChanges)
            {
                query = query.AsNoTracking();
            }

            return query
                .Include(q => q.CorrectAnswers)
                .Include("MultipleChoiceAnswer");
        }
    }
}
