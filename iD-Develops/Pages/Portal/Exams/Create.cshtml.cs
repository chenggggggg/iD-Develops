using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace iD_Develops.Pages.Portal.Exams
{
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin, Admin, Teacher")]
    public class CreateModel : PageModel
    {
        private readonly IExamService _examService;

        [BindProperty]
        public CreateExamInputModel Input { get; set; } = new();

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
            if (!ModelState.IsValid)
                return Page();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            var exam = new Exam
            {
                Name = Input.Name,
                TimeLimit = Input.TimeLimit,
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

            var result = await _examService.CreateExamAsync(exam);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to create exam draft.");
            }

            return RedirectToPage("/Portal/Examination/Edit", new { examId = exam.Id });
        }
        private static int NormalizeMaxAttempts(int? maxAttempts)
            => maxAttempts is >= 1 ? maxAttempts.Value : -1;
    }
}
