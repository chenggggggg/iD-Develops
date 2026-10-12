using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using iD_Develops.Utilities;

namespace iD_Develops.Pages.Portal.Exams
{
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin, Admin, Teacher")]
    public class CreateModel : PageModel
    {
        private readonly IExamService _examService;

        [BindProperty]
        public CreateExamInputModel Input { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public int? CourseId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? CourseSectionId { get; set; }

        public CreateModel(IExamService examService)
        {
            _examService = examService;
        }

        public void OnGet()
        {
            Input.EnsureMinimumGradeBandRows();
        }

        public async Task<IActionResult> OnPostContinueAsync()
        {
            ValidateTimeLimit();

            if (!ModelState.IsValid)
                return Page();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            var exam = new Exam
            {
                Name = Input.Name,
                TimeLimitSeconds = Input.TimeLimitSeconds,
                IntroductionPrimaryLanguage = Input.IntroductionPrimaryLanguage ?? string.Empty,
                IntroductionSecondaryLanguage = Input.IntroductionSecondaryLanguage,
                CompletionTextPrimary = Input.CompletionTextPrimary,
                CompletionTextSecondary = Input.CompletionTextSecondary,
                ResultGradeDisplayMode = ResultGradeDisplayMode.Score,
                ResultGradeCustomTextPrimary = Input.ResultGradeCustomTextPrimary,
                ResultGradeCustomTextSecondary = Input.ResultGradeCustomTextSecondary,
                GradeBands = new List<ExamGradeBand>(),
                DifficultyValue = Input.DifficultyValue!.Value,
                MaxAttempts = NormalizeMaxAttempts(Input.MaxAttempts),
                CreatedByUserId = userId,
                PublishStatus = ExamPublishStatus.Draft,
                Questions = new List<Question>()
            };

            var result = CourseSectionId.HasValue
                ? await _examService.CreateCourseExamAsync(
                    exam,
                    CourseSectionId.Value,
                    User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
                : await _examService.CreateExamAsync(exam);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to create exam draft.");
                return Page();
            }

            return RedirectToPage("/Portal/Examination/Edit", new { examId = exam.Id, returnCourseId = CourseId });
        }
        private static int NormalizeMaxAttempts(int? maxAttempts)
            => maxAttempts is >= 1 ? maxAttempts.Value : -1;

        private void ValidateTimeLimit()
        {
            if (!ExamDuration.TryParse(Input.TimeLimitInput, out var seconds))
            {
                ModelState.AddModelError(
                    "Input.TimeLimitInput",
                    "Use hh:mm:ss and enter a duration of at least one second, or leave this empty for no time limit.");
                return;
            }

            Input.TimeLimitSeconds = seconds;
            Input.TimeLimitInput = ExamDuration.Format(seconds);
        }
    }
}
