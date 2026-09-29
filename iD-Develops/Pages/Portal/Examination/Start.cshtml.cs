using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Pages.Shared.Examination;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace iD_Develops.Pages.Portal.Examination
{
    [Authorize(Policy = "PortalUser")]
    [ValidateAntiForgeryToken]
    public class StartModel : PageModel
    {
        public StartPageState PageState { get; private set; } = default!;
        public PaginationModel Pagination { get; private set; } = default!;

        // Expose these for your Start view / pagination partial
        public int AttemptsAllowed { get; private set; }
        public int AttemptsUsed { get; private set; }
        public int AttemptsLeft { get; private set; }
        public Guid? InProgressRecordId { get; private set; }

        public bool CanContinue { get; private set; }
        public bool CanStartNewAttempt { get; private set; }
        public bool CanRestart { get; private set; }

        private readonly IExamVersionService _examVersionService;
        private readonly IExamAttemptService _attemptService;
        private readonly IExamService _examService;

        public StartModel(IExamVersionService examVersionService, IExamAttemptService attemptService, IExamService examService)
        {
            _examVersionService = examVersionService;
            _attemptService = attemptService;
            _examService = examService;
        }

        public async Task<IActionResult> OnGetAsync(int examId)
        {
            if (examId <= 0)
                return RedirectToPage("/Portal/Exams/List");

            var descriptor = await _examVersionService.GetPublishedDescriptorAsync(examId);
            var isPublished = descriptor != null;

            string examTitle;
            string courseName;
            int durationSeconds;
            string introductionPrimary;
            string? introductionSecondary;
            int maxAttempts;
            int? timeLimit;
            ExamPublishStatus publishStatus;

            if (descriptor != null)
            {
                examTitle = descriptor.ExamTitle;
                courseName = descriptor.CourseName;
                durationSeconds = descriptor.DurationSeconds;
                introductionPrimary = descriptor.IntroductionPrimary;
                introductionSecondary = descriptor.IntroductionSecondary;
                maxAttempts = descriptor.MaxAttempts;
                timeLimit = descriptor.TimeLimit;
                publishStatus = ExamPublishStatus.Published;
            }
            else
            {
                if (!await CanPreviewUnpublishedExamAsync(examId))
                    return NotFound();

                var exam = await _examService.GetExamForSettingsUpdateAsync(examId);
                var headerData = await _examService.GetExamHeaderAsync(examId);
                var startSettings = await _examService.GetExamStartSettingsAsync(examId);
                if (exam == null || headerData == null || !startSettings.Exists)
                    return NotFound();

                examTitle = headerData.Value.ExamTitle;
                courseName = headerData.Value.CourseName;
                durationSeconds = headerData.Value.DurationSeconds;
                introductionPrimary = exam.IntroductionPrimaryLanguage;
                introductionSecondary = exam.IntroductionSecondaryLanguage;
                maxAttempts = startSettings.MaxAttempts;
                timeLimit = startSettings.TimeLimit;
                publishStatus = exam.PublishStatus;
            }

            var header = new ExaminationHeaderModel
            {
                ExamTitle = examTitle,
                CourseName = courseName,
                TimeLimit = durationSeconds,
                Mode = PaginationMode.Start
            };

            PageState = new StartPageState
            {
                ExamId = examId,
                Header = header,
                IntroductionPrimary = introductionPrimary ?? string.Empty,
                IntroductionSecondary = introductionSecondary ?? string.Empty,
                TimeLimit = timeLimit
            };

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            if (isPublished)
            {
                // Authoritative attempt state (handles unlimited and self-heals duplicates)
                var state = await _attemptService.GetStateAsync(userId, examId);

                AttemptsAllowed = state.MaxAttempts;
                AttemptsUsed = state.AttemptsUsed;
                AttemptsLeft = state.AttemptsLeft;
                InProgressRecordId = state.InProgressRecordId;

                CanContinue = state.CanContinue;
                CanStartNewAttempt = state.CanStart;
                CanRestart = state.CanRestart;
            }
            else
            {
                AttemptsAllowed = maxAttempts;
                AttemptsUsed = 0;
                AttemptsLeft = maxAttempts == -1 ? int.MaxValue : maxAttempts;
                InProgressRecordId = null;

                CanContinue = false;
                CanStartNewAttempt = false;
                CanRestart = false;
            }

            Pagination = new PaginationModel
            {
                ExamId = examId,
                Mode = PaginationMode.Start,
                QuestionsMetadata = Array.Empty<QuestionMetadata>(),
                CurrentQuestionId = 0,
                MaxAttempts = AttemptsAllowed,
                AttemptsLeft = AttemptsLeft,
                InProgressRecordId = InProgressRecordId,
                PublishStatus = publishStatus
            };

            return Page();
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

        public async Task<IActionResult> OnPostContinueAsync(int examId)
        {
            if (examId <= 0)
                return BadRequest();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            var result = await _attemptService.ContinueAsync(userId, examId);

            if (!result.Success || !result.RecordId.HasValue)
                return RedirectToPage(new { examId });

            return RedirectToPage("/Portal/Examination/Index", new { recordId = result.RecordId.Value });
        }

        public async Task<IActionResult> OnPostRestartAsync(int examId)
        {
            if (examId <= 0)
                return BadRequest();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            var result = await _attemptService.RestartAsync(userId, examId);

            if (!result.Success || !result.RecordId.HasValue)
                return BadRequest(new { success = false, errorMessage = result.ErrorMessage ?? "Restart failed." });

            return RedirectToPage("/Portal/Examination/Index", new { recordId = result.RecordId.Value });
        }

        // Start new attempt (button label may be "Start" or "Start again")
        public async Task<IActionResult> OnPostCreateRecordAsync(int examId)
        {
            if (examId <= 0)
                return BadRequest(new { success = false, errorMessage = "Invalid exam ID" });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            var result = await _attemptService.StartAsync(userId, examId);

            if (!result.Success || !result.RecordId.HasValue)
                return BadRequest(new { success = false, errorMessage = result.ErrorMessage ?? "Failed to start attempt." });

            return RedirectToPage("/Portal/Examination/Index", new { recordId = result.RecordId.Value });
        }
    }
}


