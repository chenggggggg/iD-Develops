using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace iD_Develops.Pages.Portal.Exams
{
    [Authorize(Policy = "PortalUser")]
    [ValidateAntiForgeryToken]
    public class ListModel : PageModel
    {
        public IReadOnlyList<Exam> Exams { get; private set; } = Array.Empty<Exam>();
        private readonly IAuthorizationService _authorizationService;
        private readonly IExamService _examService;
        private readonly IExamVersionService _examVersionService;
        private readonly IExamAttemptService _attemptService;

        public ListModel(
            IAuthorizationService authorizationService,
            IExamService examService,
            IExamVersionService examVersionService,
            IExamAttemptService attemptService)
        {
            Exams = new List<Exam>();
            _authorizationService = authorizationService;
            _examService = examService;
            _examVersionService = examVersionService;
            _attemptService = attemptService;
        }

        public async Task OnGetAsync()
        {
            if ((User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
            {
                Exams = await _examService.GetAllExamsAsync();
            }
            else if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    Exams = Array.Empty<Exam>();
                    return;
                }

                Exams = await _examService.GetExamsByTeacherAsync(userId);
            }
            else
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                Exams = string.IsNullOrWhiteSpace(userId)
                    ? Array.Empty<Exam>()
                    : await _examService.GetPublishedExamsForUserAsync(userId, HttpContext.RequestAborted);
            }
        }

        public async Task<IActionResult> OnGetLaunchInfoAsync(int examId)
        {
            if (examId <= 0)
                return BadRequest(new { success = false, errorMessage = "Invalid exam ID." });

            var descriptor = await _examVersionService.GetPublishedDescriptorAsync(examId);
            if (descriptor == null)
            {
                if (!await CanPreviewUnpublishedExamAsync(examId))
                    return NotFound(new { success = false, errorMessage = "Exam not found." });

                var exam = await _examService.GetExamForSettingsUpdateAsync(examId);
                var header = await _examService.GetExamHeaderAsync(examId);
                if (exam == null || header == null)
                    return NotFound(new { success = false, errorMessage = "Exam not found." });

                var maxAttempts = exam.MaxAttempts < 1 ? -1 : exam.MaxAttempts;

                return new JsonResult(new
                {
                    success = true,
                    examId,
                    title = header.Value.ExamTitle,
                    courseName = header.Value.CourseName,
                    timeLimit = exam.TimeLimit,
                    introductionHtml = ResolveIntroduction(
                        exam.IntroductionPrimaryLanguage,
                        exam.IntroductionSecondaryLanguage),
                    isPublished = false,
                    hasUnlimitedAttempts = maxAttempts == -1,
                    maxAttempts,
                    attemptsUsed = 0,
                    attemptsLeft = maxAttempts == -1 ? (int?)null : maxAttempts,
                    hasInProgressAttempt = false,
                    canStart = false,
                    canContinue = false,
                    canRestart = false,
                    blockReason = "Publish this exam before starting it."
                });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            var state = await _attemptService.GetStateAsync(userId, examId);

            return new JsonResult(new
            {
                success = true,
                examId,
                title = descriptor.ExamTitle,
                courseName = descriptor.CourseName,
                timeLimit = descriptor.TimeLimit,
                introductionHtml = ResolveIntroduction(
                    descriptor.IntroductionPrimary,
                    descriptor.IntroductionSecondary),
                isPublished = true,
                state.HasUnlimitedAttempts,
                state.MaxAttempts,
                state.AttemptsUsed,
                attemptsLeft = state.HasUnlimitedAttempts ? (int?)null : state.AttemptsLeft,
                hasInProgressAttempt = state.InProgressRecordId.HasValue,
                state.CanStart,
                state.CanContinue,
                state.CanRestart,
                state.BlockReason
            });
        }

        public async Task<IActionResult> OnPostLaunchExamAsync(int examId, string? launchAction)
        {
            if (examId <= 0)
                return RedirectToLaunchError("Invalid exam ID.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            AttemptActionResult result;
            if (string.Equals(launchAction, "continue", StringComparison.OrdinalIgnoreCase))
            {
                result = await _attemptService.ContinueAsync(userId, examId);
            }
            else if (string.Equals(launchAction, "restart", StringComparison.OrdinalIgnoreCase))
            {
                result = await _attemptService.RestartAsync(userId, examId);
            }
            else if (string.IsNullOrWhiteSpace(launchAction)
                || string.Equals(launchAction, "start", StringComparison.OrdinalIgnoreCase))
            {
                result = await _attemptService.StartAsync(userId, examId);
            }
            else
            {
                return RedirectToLaunchError("Invalid exam action.");
            }

            if (!result.Success || !result.RecordId.HasValue)
                return RedirectToLaunchError(result.ErrorMessage ?? "The exam could not be started.");

            return RedirectToPage("/Portal/Examination/Index", new { recordId = result.RecordId.Value });
        }

        public async Task<IActionResult> OnPostDeleteExamAsync(int examId)
        {
            var auth = await _authorizationService.AuthorizeAsync(User, "ManageExams");
            if (!auth.Succeeded)
                return Forbid();

            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(userId))
                    return Forbid();

                var canDelete = await _examService.CanTeacherManageExamAsync(examId, userId);
                if (!canDelete)
                    return Forbid();
            }

            if (examId < 1)
            {
                string errorMessage = "<p>Failed to delete exam due to invalid exam id.</p>";
                return new JsonResult(new { success = false, message = errorMessage });
            }

            var result = await _examService.DeleteExamByIdAsync(examId);

            if (result.Success)
            {
                return new JsonResult(new { success = true, message = "Exam deleted successfully." });
            }
            else
            {
                return new JsonResult(new
                {
                    success = false,
                    message = result.ErrorMessage ?? "Failed to delete exam."
                });
            }
        }

        public async Task<IActionResult> OnPostArchiveExamAsync(int examId)
        {
            var auth = await _authorizationService.AuthorizeAsync(User, "ManageExams");
            if (!auth.Succeeded)
                return Forbid();

            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(userId)
                    || !await _examService.CanTeacherManageExamAsync(examId, userId))
                {
                    return Forbid();
                }
            }

            var result = await _examService.ArchiveExamAsync(examId);
            TempData[result.Success ? "ExamActionSuccess" : "ExamActionError"] = result.Success
                ? "Exam archived. Historical attempts and results were preserved."
                : result.ErrorMessage ?? "The exam could not be archived.";

            return RedirectToPage();
        }

        private async Task<bool> CanPreviewUnpublishedExamAsync(int examId)
        {
            if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
                return true;

            if (!User.IsInRole("Teacher"))
                return false;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return !string.IsNullOrWhiteSpace(userId)
                && await _examService.CanTeacherManageExamAsync(examId, userId);
        }

        private IActionResult RedirectToLaunchError(string message)
        {
            TempData["ExamLaunchError"] = message;
            return RedirectToPage();
        }

        private static string ResolveIntroduction(string? primary, string? secondary)
            => !string.IsNullOrWhiteSpace(primary)
                ? primary
                : secondary ?? string.Empty;
    }
}
