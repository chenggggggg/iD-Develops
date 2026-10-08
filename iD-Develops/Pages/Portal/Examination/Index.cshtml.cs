using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Pages.Shared.Examination;
using iD_Develops.Pages.Shared.Examination.Questions;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace iD_Develops.Pages.Portal.Examination
{
    [Authorize(Roles = "SuperAdmin, Admin, Teacher, Student")]
    [ValidateAntiForgeryToken]
    public class IndexModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public int ExamId { get; set; }

        [BindProperty(SupportsGet = true)]
        public Guid RecordId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int QuestionId { get; set; }

        public ExamPageState PageState { get; private set; } = default!;

        // NEW: model for the pagination partial
        public PaginationModel Pagination { get; private set; } = new PaginationModel();

        private readonly IExamTakeFlowService _examTakeFlowService;
        private readonly IExamLogService _examLogService;
        private readonly IConfiguration _configuration;

        public IndexModel(
            IExamTakeFlowService examTakeFlowService,
            IExamLogService examLogService,
            IConfiguration configuration)
        {
            _examTakeFlowService = examTakeFlowService;
            _examLogService = examLogService;
            _configuration = configuration;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            var pageData = await _examTakeFlowService.LoadPageAsync(
                ExamId > 0 ? ExamId : null,
                RecordId,
                QuestionId,
                userId,
                requireOwnership: true);

            if (!pageData.Exists)
                return NotFound();

            if (!pageData.UserMatches)
                return Forbid();

            if (pageData.ExamStatus == ExamStatus.Completed || pageData.ExamStatus == ExamStatus.Overdue)
                return RedirectToRoute(
                    ApplicationHostPageRouteModelConvention.PortalExamCompletedRouteName,
                    new { recordId = RecordId });

            ExamId = pageData.ExamId;
            QuestionId = pageData.QuestionId;

            var descriptor = pageData.Descriptor;
            if (descriptor == null)
                return NotFound();

            ViewData["StoragePrefix"] = _configuration["Storage:Prefix"] ?? "dev";

            var header = new ExaminationHeaderModel
            {
                ExamTitle = descriptor.ExamTitle,
                CourseName = descriptor.CourseName,
                TimeLimit = descriptor.DurationSeconds,
                EndDateTime = pageData.EndDateTime,
                Mode = PaginationMode.Exam
            };

            // NEW: populate pagination model for the partial (exam mode here)
            Pagination = new PaginationModel
            {
                QuestionsMetadata = pageData.QuestionsMetadata,
                CurrentQuestionId = QuestionId,
                Mode = PaginationMode.Exam
            };

            // existing page state
            PageState = new ExamPageState
            {
                ExamId = ExamId,
                RecordId = RecordId,
                QuestionId = QuestionId,

                ExamStatus = pageData.ExamStatus,
                CurrentQuestion = pageData.CurrentQuestion,
                CurrentSavedAnswerText = pageData.CurrentSavedAnswerText,
                QuestionsMetadata = pageData.QuestionsMetadata,
                Header = header
            };

            return Page();
        }

        // Loads a question shell for Exam (Take) mode.
        public async Task<IActionResult> OnGetQuestionShellAsync(int? examId, int questionId, Guid recordId)
        {
            if (questionId <= 0 || recordId == Guid.Empty)
                return BadRequest();

            // Must be authenticated
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            var questionData = await _examTakeFlowService.LoadQuestionAsync(
                examId,
                recordId,
                questionId,
                userId,
                requireOwnership: true);

            if (!questionData.Exists)
                return NotFound();

            if (!questionData.UserMatches)
                return Forbid();

            if (questionData.ExamStatus != ExamStatus.InProgress)
                return StatusCode(StatusCodes.Status409Conflict, new { success = false, message = "Exam attempt is no longer in progress." });

            var question = questionData.Question;
            if (question == null)
                return NotFound();

            var effectiveExamId = questionData.ExamId;
            if (effectiveExamId <= 0)
                return NotFound();

            ViewData["StoragePrefix"] = _configuration["Storage:Prefix"] ?? "dev";

            LogExamActivity(
                action: "Question Viewed",
                message: "Participant loaded a question in take mode.",
                examId: effectiveExamId,
                questionId: questionId,
                timestamp: DateTime.UtcNow,
                answerText: string.Empty);

            return Partial("/Pages/Shared/Examination/Questions/_QuestionShell.cshtml", new QuestionShellModel
            {
                Question = question,
                SavedAnswerText = questionData.SavedAnswerText,
                Mode = QuestionRenderMode.Take
            });
        }

        public async Task<IActionResult> OnPostSubmitAnswer(IFormCollection formData)
        {
            var result = await _examTakeFlowService.SaveAnswerAsync(formData);
            var timestamp = DateTime.UtcNow;

            if (result.ExamId.HasValue && result.QuestionId > 0)
            {
                LogExamActivity(
                    "Saving answer",
                    $"ParticipantAnswer is being saved. Source: {result.Source}.",
                    result.ExamId.Value,
                    result.QuestionId,
                    timestamp,
                    result.AnswerText);
            }

            if (!result.Success)
            {
                if (result.SaveRejected && result.ExamId.HasValue && result.QuestionId > 0)
                {
                    LogExamActivity(
                        "Save rejected",
                        $"ParticipantAnswer save was rejected (likely deadline passed). Source: {result.Source}.",
                        result.ExamId.Value,
                        result.QuestionId,
                        timestamp,
                        result.AnswerText);
                }

                return StatusCode(result.StatusCode, new { success = false, message = result.ErrorMessage });
            }

            if (result.ExamId.HasValue && result.QuestionId > 0)
            {
                LogExamActivity(
                    "Answer saved",
                    $"ParticipantAnswer saved successfully. Source: {result.Source}.",
                    result.ExamId.Value,
                    result.QuestionId,
                    timestamp,
                    result.AnswerText);
            }

            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostValidateMissingQuestionsAsync(int? examId, Guid recordId)
        {
            var result = await _examTakeFlowService.ValidateMissingQuestionsAsync(examId, recordId);
            if (!result.Success && !result.MissingQuestionNumbers.Any())
                return new JsonResult(new { success = false, errorMessage = result.ErrorMessage });

            if (result.MissingQuestionNumbers.Any())
                return new JsonResult(new { success = false, missingQuestionNumbers = result.MissingQuestionNumbers });

            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostSubmitExamAsync(IFormCollection formData)
        {
            var result = await _examTakeFlowService.SubmitExamAsync(
                formData,
                recordId => Url.RouteUrl(
                    ApplicationHostPageRouteModelConvention.PortalExamCompletedRouteName,
                    new { recordId }));

            return new JsonResult(new
            {
                success = result.Success,
                errorMessage = result.ErrorMessage,
                redirectUrl = result.RedirectUrl
            });
        }

        private void LogExamActivity(string action, string message, int examId, int questionId, DateTime timestamp, string? answerText)
        {
            var examLog = new ExamLog
            {
                ApplicationUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                Timestamp = timestamp,
                ExamId = examId,
                QuestionId = questionId,
                Action = action,
                LogMessage = message,
                AnswerText = TruncateForLog(answerText)
            };

            _examLogService.EnqueueSaveExamLog(examLog);
        }

        private static string TruncateForLog(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            const int maxLength = 1000;
            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }

}




