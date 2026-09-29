using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Pages.Shared.Examination;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Globalization;

namespace iD_Develops.Pages.Portal.Examination
{
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin, Admin, Teacher")]
    public class SettingsModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public int ExamId { get; set; }

        [BindProperty]
        public CreateExamInputModel Settings { get; set; } = new();

        [TempData(Key = "ExamSettingsStatusMessage")]
        public string? StatusMessage { get; set; }

        private readonly IExamEditFlowService _examEditFlowService;
        private readonly IExamEditMutationService _examEditMutationService;
        private readonly IExamService _examService;
        private readonly IQuestionService _questionService;

        public double MaximumScore { get; private set; }

        public ExaminationHeaderModel Header { get; private set; } = new ExaminationHeaderModel
        {
            ExamTitle = "Exam Settings",
            CourseName = string.Empty,
            Mode = PaginationMode.Start
        };

        public SettingsModel(
            IExamEditFlowService examEditFlowService,
            IExamEditMutationService examEditMutationService,
            IExamService examService,
            IQuestionService questionService)
        {
            _examEditFlowService = examEditFlowService;
            _examEditMutationService = examEditMutationService;
            _examService = examService;
            _questionService = questionService;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await CanManageExamAsync(ExamId))
                return Forbid();

            var pageData = await _examEditFlowService.LoadPageAsync(ExamId, 0, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (pageData == null)
                return NotFound();

            Header = pageData.Header;
            Header.Mode = PaginationMode.Start;
            Settings = pageData.Settings;
            Settings.EnsureMinimumGradeBandRows();
            MaximumScore = await LoadMaximumScoreAsync(ExamId);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!await CanManageExamAsync(ExamId))
                return Forbid();

            Settings.EnsureMinimumGradeBandRows();
            MaximumScore = await LoadMaximumScoreAsync(ExamId);
            ValidateGradeBands();

            if (!ModelState.IsValid)
            {
                var pageData = await _examEditFlowService.LoadPageAsync(ExamId, 0, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
                if (pageData == null)
                    return NotFound();

                Header = pageData.Header;
                Header.Mode = PaginationMode.Start;
                return Page();
            }

            var result = await _examEditMutationService.SaveSettingsAsync(ExamId, Settings, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (!result.Success)
                return StatusCode(result.StatusCode);

            StatusMessage = "Settings saved.";
            return RedirectToPage(new { ExamId });
        }

        private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private async Task<double> LoadMaximumScoreAsync(int examId)
        {
            var questions = await _questionService.GetQuestionsForExamForEditAsync(examId);
            return questions
                .Select(q => q.Score.HasValue && q.Score.Value > 0 ? q.Score.Value : 1d)
                .DefaultIfEmpty(0d)
                .Sum();
        }

        private void ValidateGradeBands()
        {
            if (!Settings.DisplayGradeOnResults)
                return;

            if (MaximumScore <= 0d)
            {
                ModelState.AddModelError(nameof(Settings.DisplayGradeOnResults), "Add at least one question with points before enabling result grading.");
                return;
            }

            var completedBands = new List<(int Index, double MaximumScore)>();
            var usedGradeNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < Settings.ResultGradeBands.Count; i++)
            {
                var band = Settings.ResultGradeBands[i];
                var hasScore = band.MaximumScore.HasValue;
                var hasLabel = !string.IsNullOrWhiteSpace(band.LabelPrimary);

                if (!hasScore && !hasLabel)
                    continue;

                if (!hasScore)
                {
                    ModelState.AddModelError($"Settings.ResultGradeBands[{i}].MaximumScore", "Enter the highest score for this grade.");
                    continue;
                }

                if (!hasLabel)
                {
                    ModelState.AddModelError($"Settings.ResultGradeBands[{i}].LabelPrimary", "Enter the grade name shown to students.");
                }

                var maximumScore = band.MaximumScore!.Value;
                if (maximumScore < 0d)
                {
                    ModelState.AddModelError($"Settings.ResultGradeBands[{i}].MaximumScore", "Upper score must be 0 or higher.");
                }

                if (!HasAtMostOneDecimalPlace(maximumScore))
                {
                    ModelState.AddModelError($"Settings.ResultGradeBands[{i}].MaximumScore", "Use at most 1 decimal place, for example 0.5, 1, or 1.5.");
                }

                if (maximumScore > MaximumScore)
                {
                    ModelState.AddModelError(
                        $"Settings.ResultGradeBands[{i}].MaximumScore",
                        $"Upper score cannot be higher than the current maximum score of {FormatScore(MaximumScore)}.");
                }

                var normalizedGradeName = band.LabelPrimary?.Trim();
                if (!string.IsNullOrWhiteSpace(normalizedGradeName))
                {
                    if (usedGradeNames.TryGetValue(normalizedGradeName, out var existingIndex))
                    {
                        ModelState.AddModelError(
                            $"Settings.ResultGradeBands[{i}].LabelPrimary",
                            $"This grade name is already used in row {existingIndex + 1}.");
                    }
                    else
                    {
                        usedGradeNames[normalizedGradeName] = i;
                    }
                }

                completedBands.Add((i, Math.Round(maximumScore, 1, MidpointRounding.AwayFromZero)));
            }

            for (var i = 1; i < completedBands.Count; i++)
            {
                var previousUpper = completedBands[i - 1].MaximumScore;
                var currentUpper = completedBands[i].MaximumScore;

                if (currentUpper <= previousUpper)
                {
                    ModelState.AddModelError(
                        $"Settings.ResultGradeBands[{completedBands[i].Index}].MaximumScore",
                        "Each next 'Up to score' must be higher than the row above so grade ranges do not overlap.");
                }
            }

            var sortedBands = completedBands
                .OrderBy(b => b.MaximumScore)
                .ToList();

            for (var i = 0; i < sortedBands.Count; i++)
            {
                if (i > 0 && Math.Abs(sortedBands[i].MaximumScore - sortedBands[i - 1].MaximumScore) < 0.0001d)
                {
                    ModelState.AddModelError(
                        $"Settings.ResultGradeBands[{sortedBands[i].Index}].MaximumScore",
                        $"This upper score is already used in row {sortedBands[i - 1].Index + 1}.");
                }
            }

            if (sortedBands.Count == 0)
            {
                ModelState.AddModelError(nameof(Settings.DisplayGradeOnResults), "Please fill in at least 2 grades. The light example text in the boxes is only a hint.");
                return;
            }

            if (sortedBands.Count < 2)
            {
                ModelState.AddModelError(nameof(Settings.DisplayGradeOnResults), "Please fill in at least 2 complete grades. Each grade needs both a score and a grade name.");
            }

            var highestUpper = sortedBands[^1].MaximumScore;
            if (Math.Abs(highestUpper - MaximumScore) > 0.0001d)
            {
                ModelState.AddModelError(
                    $"Settings.ResultGradeBands[{sortedBands[^1].Index}].MaximumScore",
                    $"The last grade must end at the current maximum score of {FormatScore(MaximumScore)} so every score gets a grade.");
            }

            if (sortedBands[0].MaximumScore < 0d)
            {
                ModelState.AddModelError(
                    $"Settings.ResultGradeBands[{sortedBands[0].Index}].MaximumScore",
                    "The first grade must cover scores starting at 0.");
            }
        }

        private static bool HasAtMostOneDecimalPlace(double value)
            => Math.Abs(value * 10d - Math.Round(value * 10d, 0, MidpointRounding.AwayFromZero)) < 0.0001d;

        private static string FormatScore(double value)
            => value.ToString("0.#", CultureInfo.InvariantCulture);

        private async Task<bool> CanManageExamAsync(int examId)
        {
            if (examId <= 0)
                return false;

            if ((User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
                return true;

            if (string.IsNullOrWhiteSpace(CurrentUserId))
                return false;

            return await _examService.CanTeacherManageExamAsync(examId, CurrentUserId);
        }
    }
}
