using System.Globalization;
using System.Text;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace iD_Develops.Pages.Examination
{
    public class ResultsModel : PageModel
    {
        private const string LevelTestPublicSlug = "level-test";
        private const string PublicResultPurpose = "LevelTestPublicResult:v1";

        public sealed record ResultSummaryViewModel(
            Guid RecordId,
            string ExamName,
            DateTime StartDateTime,
            DateTime? EndDateTime,
            double Score,
            double MaxScore,
            int CorrectAnswers,
            int TotalQuestions,
            string GradeLabel,
            string GradeValue,
            string? CompletionText,
            string AdviceTitle,
            string AdviceText,
            int ScorePercentage);

        public sealed record QuestionReviewItem(
            int QuestionNumber,
            string QuestionHtml,
            string ResultLabel,
            string ResultBadgeClass,
            string SubmittedAnswer,
            string CorrectAnswer,
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
        public string? Token { get; set; }

        public ResultSummaryViewModel? Summary { get; private set; }
        public IReadOnlyList<QuestionReviewItem> Questions { get; private set; } = Array.Empty<QuestionReviewItem>();
        public bool IsInvalidResult { get; private set; }

        private readonly ApplicationDbContext _dbContext;
        private readonly IExamVersionService _examVersionService;
        private readonly IParticipantAnswerService _participantAnswerService;
        private readonly IExamEvaluationService _examEvaluationService;
        private readonly IDataProtector _publicResultProtector;
        private readonly IStringLocalizer<ResultsModel> _localizer;

        public ResultsModel(
            ApplicationDbContext dbContext,
            IExamVersionService examVersionService,
            IParticipantAnswerService participantAnswerService,
            IExamEvaluationService examEvaluationService,
            IDataProtectionProvider dataProtectionProvider,
            IStringLocalizer<ResultsModel> localizer)
        {
            _dbContext = dbContext;
            _examVersionService = examVersionService;
            _participantAnswerService = participantAnswerService;
            _examEvaluationService = examEvaluationService;
            _publicResultProtector = dataProtectionProvider.CreateProtector(PublicResultPurpose);
            _localizer = localizer;
        }

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            ViewData["Title"] = _localizer["Title"];
            ViewData["BodyClass"] = "public-result-page";

            if (RecordId == Guid.Empty || string.IsNullOrWhiteSpace(Token) || !IsValidPublicResultToken(RecordId, Token))
            {
                IsInvalidResult = true;
                return Page();
            }

            var record = await _dbContext.Records
                .AsNoTracking()
                .Include(r => r.Exam)
                .FirstOrDefaultAsync(r => r.Id == RecordId && !r.IsDeleted, cancellationToken);

            if (!IsDisplayableLevelTestRecord(record))
            {
                IsInvalidResult = true;
                return Page();
            }

            var exam = await _examVersionService.GetExamForEvaluationAsync(record!.ExamId, record.ExamVersionId);
            if (exam == null || !string.Equals(exam.PublicSlug, LevelTestPublicSlug, StringComparison.OrdinalIgnoreCase))
            {
                IsInvalidResult = true;
                return Page();
            }

            var answers = await _participantAnswerService.GetParticipantAnswersByRecordIdAsync(RecordId) ?? new List<ParticipantAnswer>();
            var evaluation = _examEvaluationService.EvaluateExam(exam, answers);
            var answersByQuestionId = evaluation.ParticipantAnswers.ToDictionary(a => a.QuestionId);
            var orderedQuestions = evaluation.Questions.OrderBy(q => q.QuestionNumber).ToList();
            var correctCount = evaluation.ParticipantAnswers.Count(a => a.IsCorrect);

            Summary = BuildSummary(record, exam, evaluation.Score, evaluation.MaxScore, correctCount, orderedQuestions.Count);
            Questions = orderedQuestions
                .Select(question => BuildQuestionReviewItem(question, answersByQuestionId))
                .ToList();

            return Page();
        }

        public static string CreatePublicResultToken(IDataProtectionProvider dataProtectionProvider, Guid recordId, string email)
        {
            var protector = dataProtectionProvider.CreateProtector(PublicResultPurpose);
            var payload = $"{recordId:N}|{email.Trim().ToLowerInvariant()}";
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protector.Protect(payload)));
        }

        private ResultSummaryViewModel BuildSummary(iD_Develops.Models.Record record, Exam exam, double score, double maxScore, int correctCount, int totalQuestions)
        {
            maxScore = maxScore > 0 ? maxScore : ResolveMaxScore(exam);
            var grade = ResolveLevelTestResult(score);
            var scorePercentage = maxScore <= 0
                ? 0
                : Math.Clamp((int)Math.Round(score / maxScore * 100d, MidpointRounding.AwayFromZero), 0, 100);

            return new ResultSummaryViewModel(
                record.Id,
                exam.Name,
                record.StartDateTime,
                record.EndDateTime,
                score,
                maxScore,
                correctCount,
                totalQuestions,
                _localizer["LevelLabel"],
                grade.Level,
                ResolveLocalized(exam.CompletionTextPrimary, exam.CompletionTextSecondary),
                _localizer["AdviceTitle"],
                grade.Advice,
                scorePercentage);
        }

        private QuestionReviewItem BuildQuestionReviewItem(Question question, IReadOnlyDictionary<int, ParticipantAnswer> answersByQuestionId)
        {
            answersByQuestionId.TryGetValue(question.Id, out var participantAnswer);
            var isCorrect = participantAnswer?.IsCorrect == true;

            return new QuestionReviewItem(
                question.QuestionNumber,
                question.Text,
                isCorrect ? _localizer["CorrectLabel"] : _localizer["ReviewLabel"],
                isCorrect ? "public-result-badge-correct" : "public-result-badge-review",
                FormatAnswer(question, participantAnswer?.AnswerText, emptyFallback: _localizer["NoAnswerSubmitted"]),
                FormatCorrectAnswers(question),
                question.Feedback,
                question.FunFact,
                BuildMultipleChoiceOptions(question, participantAnswer?.AnswerText));
        }

        private bool IsValidPublicResultToken(Guid recordId, string token)
        {
            try
            {
                var protectedPayload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
                var payload = _publicResultProtector.Unprotect(protectedPayload);
                var parts = payload.Split('|', 2, StringSplitOptions.TrimEntries);
                return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var tokenRecordId) && tokenRecordId == recordId;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDisplayableLevelTestRecord(iD_Develops.Models.Record? record)
        {
            return record != null
                && record.ExamStatus == ExamStatus.Completed
                && string.Equals(record.Exam?.PublicSlug, LevelTestPublicSlug, StringComparison.OrdinalIgnoreCase);
        }

        private (string Level, string Advice) ResolveLevelTestResult(double score)
        {
            if (score >= 51 && score <= 60)
            {
                return (
                    _localizer["GradeA1"],
                    _localizer["GradeA1Advice"]);
            }

            if (score >= 45 && score <= 50)
            {
                return (
                    _localizer["GradeReadyA1A2"],
                    _localizer["GradeReadyA1A2Advice"]);
            }

            return (
                _localizer["GradeBelowA1"],
                _localizer["GradeBelowA1Advice"]);
        }

        private string? ResolveLocalized(string? primary, string? secondary)
        {
            var isDutch = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("nl", StringComparison.OrdinalIgnoreCase);
            return isDutch
                ? (string.IsNullOrWhiteSpace(primary) ? secondary : primary)
                : (string.IsNullOrWhiteSpace(secondary) ? primary : secondary);
        }

        private static string FormatNumber(double value)
            => Math.Abs(value % 1) < 0.001 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);

        private static double ResolveQuestionScore(Question question)
            => question.Score.HasValue && question.Score.Value > 0 ? question.Score.Value : 1d;

        private static double ResolveMaxScore(Exam exam)
        {
            return exam.Questions
                .Select(ResolveQuestionScore)
                .DefaultIfEmpty(0d)
                .Sum();
        }

        private string FormatCorrectAnswers(Question question)
        {
            if (question.CorrectAnswers == null || question.CorrectAnswers.Count == 0)
                return _localizer["NoModelAnswer"];

            return string.Join(" / ", question.CorrectAnswers.Select(answer => FormatAnswer(question, answer.Text, emptyFallback: _localizer["NoModelAnswer"])));
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
    }
}
