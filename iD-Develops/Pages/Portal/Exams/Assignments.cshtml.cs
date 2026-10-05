using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Exams
{
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public sealed class AssignmentsModel : PageModel
    {
        private readonly IExamAssignmentService _assignmentService;

        public AssignmentsModel(IExamAssignmentService assignmentService)
        {
            _assignmentService = assignmentService;
        }

        public ExamAssignmentPageData Data { get; private set; } = null!;

        [BindProperty]
        [Required]
        public string SelectedUserId { get; set; } = string.Empty;

        [BindProperty]
        public DateTime? UnlockAtUtc { get; set; }

        [BindProperty]
        public DateTime? DueAtUtc { get; set; }

        [BindProperty]
        [Range(1, 1000)]
        public int AdditionalAttempts { get; set; } = 1;

        [BindProperty]
        [MaxLength(500)]
        public string? AttemptReason { get; set; }

        public async Task<IActionResult> OnGetAsync(int examId, CancellationToken cancellationToken)
            => await LoadAsync(examId, cancellationToken) ? Page() : Forbid();

        public async Task<IActionResult> OnPostAssignAsync(int examId, CancellationToken cancellationToken)
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actor))
                return Challenge();

            var unlockAtUtc = ToUtcFromAmsterdam(UnlockAtUtc);
            var dueAtUtc = ToUtcFromAmsterdam(DueAtUtc);
            if ((UnlockAtUtc.HasValue && !unlockAtUtc.HasValue) || (DueAtUtc.HasValue && !dueAtUtc.HasValue))
            {
                TempData["ErrorMessage"] = "That time does not exist in Europe/Amsterdam because of the daylight-saving transition.";
                return RedirectToPage(new { examId });
            }

            var result = await _assignmentService.AssignAsync(
                examId,
                SelectedUserId,
                unlockAtUtc,
                dueAtUtc,
                actor,
                CanManageAll(),
                cancellationToken);
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success
                ? "Exam assigned."
                : result.ErrorMessage;
            return RedirectToPage(new { examId });
        }

        public async Task<IActionResult> OnPostRemoveAsync(int examId, string userId, CancellationToken cancellationToken)
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actor))
                return Challenge();

            var result = await _assignmentService.RemoveAsync(
                examId,
                userId,
                actor,
                CanManageAll(),
                cancellationToken);
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success
                ? "Direct assignment removed."
                : result.ErrorMessage;
            return RedirectToPage(new { examId });
        }

        public async Task<IActionResult> OnPostGrantAttemptsAsync(int examId, CancellationToken cancellationToken)
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actor))
                return Challenge();

            var result = await _assignmentService.GrantAttemptsAsync(
                examId,
                SelectedUserId,
                AdditionalAttempts,
                AttemptReason,
                actor,
                CanManageAll(),
                cancellationToken);
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success
                ? "Additional attempts granted."
                : result.ErrorMessage;
            return RedirectToPage(new { examId });
        }

        private async Task<bool> LoadAsync(int examId, CancellationToken cancellationToken)
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actor))
                return false;
            var data = await _assignmentService.GetPageAsync(examId, actor, CanManageAll(), cancellationToken);
            if (data == null)
                return false;
            Data = data;
            return true;
        }

        private bool CanManageAll() => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        public static DateTime? ToAmsterdam(DateTime? utcValue)
            => utcValue.HasValue
                ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcValue.Value, DateTimeKind.Utc), AmsterdamTimeZone)
                : null;

        private static DateTime? ToUtcFromAmsterdam(DateTime? localValue)
        {
            if (!localValue.HasValue)
                return null;
            var unspecified = DateTime.SpecifyKind(localValue.Value, DateTimeKind.Unspecified);
            return AmsterdamTimeZone.IsInvalidTime(unspecified)
                ? null
                : TimeZoneInfo.ConvertTimeToUtc(unspecified, AmsterdamTimeZone);
        }

        private static TimeZoneInfo AmsterdamTimeZone { get; } =
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    }
}
