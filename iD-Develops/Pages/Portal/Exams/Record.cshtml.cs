using System.Globalization;
using System.Security.Claims;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Pages.Portal.Exams
{
    [Authorize(Policy = "PortalUser")]
    public class RecordModel : PageModel
    {
        public sealed record RecordSummaryViewModel(
            Guid RecordId,
            string ExamName,
            int? ExamVersionNumber,
            ExamStatus ExamStatus,
            DateTime StartDateTime,
            DateTime? EndDateTime,
            double Score,
            double MaxScore,
            int CorrectAnswers,
            int TotalQuestions,
            string GradeLabel,
            string GradeValue,
            string? CompletionText);

        public sealed record QuestionReviewItem(
            int QuestionNumber,
            string? ScenarioHtml,
            string QuestionHtml,
            double QuestionScore,
            double AwardedScore,
            string ResultLabel,
            string ResultBadgeClass,
            string? ImageUrl,
            string? MessageBeforeQuestionHtml,
            string SubmittedAnswer,
            string CorrectAnswer,
            bool IsCorrect,
            string? FeedbackHtml,
            string? FunFactHtml,
            IReadOnlyList<MultipleChoiceOptionReviewItem> MultipleChoiceOptions);

        public sealed record MultipleChoiceOptionReviewItem(
            string Key,
            string Text,
            bool IsSelected,
            bool IsCorrect);

        [BindProperty(SupportsGet = true)]
        public Guid RecordId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        public const int PageSize = 5;

        public RecordSummaryViewModel? Summary { get; private set; }
        public IReadOnlyList<QuestionReviewItem> Questions { get; private set; } = Array.Empty<QuestionReviewItem>();
        public int TotalPages { get; private set; }
        public int TotalQuestions { get; private set; }

        private readonly ApplicationDbContext _dbContext;
        private readonly IExamVersionService _examVersionService;
        private readonly IParticipantAnswerService _participantAnswerService;
        private readonly IExamEvaluationService _examEvaluationService;
        private readonly IStoragePublicUrlService _storagePublicUrlService;

        public RecordModel(
            ApplicationDbContext dbContext,
            IExamVersionService examVersionService,
            IParticipantAnswerService participantAnswerService,
            IExamEvaluationService examEvaluationService,
            IStoragePublicUrlService storagePublicUrlService)
        {
            _dbContext = dbContext;
            _examVersionService = examVersionService;
            _participantAnswerService = participantAnswerService;
            _examEvaluationService = examEvaluationService;
            _storagePublicUrlService = storagePublicUrlService;
        }

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            if (RecordId == Guid.Empty)
                return RedirectToPage("/Portal/Exams/Results");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge();

            var record = await _dbContext.Records
                .AsNoTracking()
                .Include(r => r.Exam)
                .Include(r => r.ExamVersion)
                .FirstOrDefaultAsync(r => r.Id == RecordId && !r.IsDeleted, cancellationToken);

            if (record == null)
                return NotFound();

            if (!string.Equals(record.UserId, userId, StringComparison.Ordinal))
                return Forbid();

            var exam = await _examVersionService.GetExamForEvaluationAsync(record.ExamId, record.ExamVersionId);
            if (exam == null)
                return NotFound();

            var answers = await _participantAnswerService.GetParticipantAnswersByRecordIdAsync(RecordId) ?? new List<ParticipantAnswer>();
            var evaluation = _examEvaluationService.EvaluateExam(exam, answers);
            var answersByQuestionId = evaluation.ParticipantAnswers.ToDictionary(a => a.QuestionId);
            var correctCount = evaluation.ParticipantAnswers.Count(a => a.IsCorrect);
            var orderedQuestions = evaluation.Questions.OrderBy(q => q.QuestionNumber).ToList();

            TotalQuestions = orderedQuestions.Count;
            TotalPages = Math.Max(1, (int)Math.Ceiling(TotalQuestions / (double)PageSize));
            PageNumber = Math.Clamp(PageNumber <= 0 ? 1 : PageNumber, 1, TotalPages);

            Summary = BuildSummary(record, exam, evaluation.Score, correctCount, orderedQuestions.Count);
            Questions = orderedQuestions
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .Select(question => BuildQuestionReviewItem(question, answersByQuestionId))
                .ToList();

            return Page();
        }

        private RecordSummaryViewModel BuildSummary(Models.Record record, Exam exam, double score, int correctCount, int totalQuestions)
        {
            var maxScore = ResolveMaxScore(exam);
            var grade = ResolveGrade(exam, score, maxScore, correctCount, totalQuestions);

            return new RecordSummaryViewModel(
                record.Id,
                exam.Name,
                record.ExamVersion?.VersionNumber,
                record.ExamStatus,
                record.StartDateTime,
                record.EndDateTime,
                score,
                maxScore,
                correctCount,
                totalQuestions,
                grade.Label,
                grade.Value,
                ResolveLocalized(exam.CompletionTextPrimary, exam.CompletionTextSecondary));
        }

        private QuestionReviewItem BuildQuestionReviewItem(Question question, IReadOnlyDictionary<int, ParticipantAnswer> answersByQuestionId)
        {
            answersByQuestionId.TryGetValue(question.Id, out var participantAnswer);
            var questionScore = ResolveQuestionScore(question);
            var awardedScore = participantAnswer?.IsCorrect == true ? questionScore : 0d;

            return new QuestionReviewItem(
                question.QuestionNumber,
                question.Scenario,
                question.Text,
                questionScore,
                awardedScore,
                ResolveResultLabel(awardedScore, questionScore),
                ResolveResultBadgeClass(awardedScore, questionScore),
                ResolveQuestionImageUrl(question.ImageReference),
                question.MessageBeforeQuestion,
                FormatAnswer(question, participantAnswer?.AnswerText, emptyFallback: "No answer submitted"),
                FormatCorrectAnswers(question),
                participantAnswer?.IsCorrect == true,
                question.Feedback,
                question.FunFact,
                BuildMultipleChoiceOptions(question, participantAnswer?.AnswerText));
        }

        private (string Label, string Value) ResolveGrade(Exam exam, double score, double maxScore, int correctCount, int totalQuestions)
        {
            return exam.ResultGradeDisplayMode switch
            {
                ResultGradeDisplayMode.CorrectAnswers => ("Correct answers", $"{correctCount} / {totalQuestions}"),
                ResultGradeDisplayMode.GradeBands when TryResolveGradeBand(exam, score, out var gradeBand)
                    => ("Grade", gradeBand!),
                ResultGradeDisplayMode.CustomText when !string.IsNullOrWhiteSpace(ResolveLocalized(exam.ResultGradeCustomTextPrimary, exam.ResultGradeCustomTextSecondary))
                    => ("Grade", ResolveLocalized(exam.ResultGradeCustomTextPrimary, exam.ResultGradeCustomTextSecondary)!),
                _ => ("Score", $"{FormatNumber(score)} / {FormatNumber(maxScore)}")
            };
        }

        private bool TryResolveGradeBand(Exam exam, double score, out string? label)
        {
            label = exam.GradeBands
                .OrderByDescending(b => b.MinimumScore)
                .Where(b => score >= b.MinimumScore)
                .Select(b => ResolveLocalized(b.LabelPrimary, b.LabelSecondary))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            return !string.IsNullOrWhiteSpace(label);
        }

        private string? ResolveLocalized(string? primary, string? secondary)
        {
            var isDutch = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("nl", StringComparison.OrdinalIgnoreCase);
            return isDutch
                ? (string.IsNullOrWhiteSpace(primary) ? secondary : primary)
                : (string.IsNullOrWhiteSpace(secondary) ? primary : secondary);
        }

        private static string FormatNumber(double value)
            => Math.Abs(value % 1) < 0.001 ? value.ToString("0") : value.ToString("0.##");

        private static string FormatCorrectAnswers(Question question)
        {
            if (question.CorrectAnswers == null || question.CorrectAnswers.Count == 0)
                return "No model answer provided";

            return string.Join(" / ", question.CorrectAnswers.Select(answer => FormatAnswer(question, answer.Text, emptyFallback: "No model answer provided")));
        }

        private static string FormatAnswer(Question question, string? answerText, string emptyFallback)
        {
            if (string.IsNullOrWhiteSpace(answerText))
                return emptyFallback;

            if (question is MultipleChoiceQuestion multipleChoiceQuestion)
            {
                var key = answerText.Trim().ToUpperInvariant();
                var optionText = key switch
                {
                    "A" => multipleChoiceQuestion.MultipleChoiceAnswer?.AnswerA,
                    "B" => multipleChoiceQuestion.MultipleChoiceAnswer?.AnswerB,
                    "C" => multipleChoiceQuestion.MultipleChoiceAnswer?.AnswerC,
                    "D" => multipleChoiceQuestion.MultipleChoiceAnswer?.AnswerD,
                    _ => null
                };

                return string.IsNullOrWhiteSpace(optionText) ? key : $"{key}. {optionText}";
            }

            return answerText.Trim();
        }

        private static IReadOnlyList<MultipleChoiceOptionReviewItem> BuildMultipleChoiceOptions(Question question, string? submittedAnswer)
        {
            if (question is not MultipleChoiceQuestion multipleChoiceQuestion || multipleChoiceQuestion.MultipleChoiceAnswer == null)
                return Array.Empty<MultipleChoiceOptionReviewItem>();

            var submittedKey = NormalizeAnswerKey(submittedAnswer);
            var correctKeys = question.CorrectAnswers?
                .Select(answer => NormalizeAnswerKey(answer.Text))
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var options = new (string Key, string? Text)[]
            {
                ("A", multipleChoiceQuestion.MultipleChoiceAnswer.AnswerA),
                ("B", multipleChoiceQuestion.MultipleChoiceAnswer.AnswerB),
                ("C", multipleChoiceQuestion.MultipleChoiceAnswer.AnswerC),
                ("D", multipleChoiceQuestion.MultipleChoiceAnswer.AnswerD)
            };

            return options
                .Where(option => !string.IsNullOrWhiteSpace(option.Text))
                .Select(option => new MultipleChoiceOptionReviewItem(
                    option.Key,
                    option.Text!.Trim(),
                    string.Equals(submittedKey, option.Key, StringComparison.OrdinalIgnoreCase),
                    correctKeys.Contains(option.Key)))
                .ToList();
        }

        private static string? NormalizeAnswerKey(string? answerText)
        {
            if (string.IsNullOrWhiteSpace(answerText))
                return null;

            var trimmed = answerText.Trim();
            if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
                return trimmed.ToUpperInvariant();

            return null;
        }

        private string? ResolveQuestionImageUrl(string? imageReference)
        {
            var imageRef = (imageReference ?? string.Empty).Trim().TrimStart('/');
            if (string.IsNullOrWhiteSpace(imageRef))
                return null;

            return _storagePublicUrlService.ResolvePublicUrl(imageRef);
        }

        private static double ResolveQuestionScore(Question question)
            => question.Score.HasValue && question.Score.Value > 0 ? question.Score.Value : 1d;

        private static string ResolveResultLabel(double awardedScore, double questionScore)
        {
            if (awardedScore <= 0d)
                return "Incorrect";

            if (awardedScore + 0.001d < questionScore)
                return "Partially correct";

            return "Correct";
        }

        private static string ResolveResultBadgeClass(double awardedScore, double questionScore)
        {
            if (awardedScore <= 0d)
                return "record-result-pill-incorrect";

            if (awardedScore + 0.001d < questionScore)
                return "record-result-pill-partial";

            return "record-result-pill-correct";
        }

        private static double ResolveMaxScore(Exam exam)
        {
            var summedQuestionScore = exam.Questions
                .Select(ResolveQuestionScore)
                .DefaultIfEmpty(0d)
                .Sum();

            return summedQuestionScore;
        }
    }
}
