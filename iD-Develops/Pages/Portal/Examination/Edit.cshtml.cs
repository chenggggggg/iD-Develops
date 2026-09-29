using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Pages.Shared.Examination;
using iD_Develops.Pages.Shared.Examination.Questions;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace iD_Develops.Pages.Portal.Examination
{
    [ValidateAntiForgeryToken]
    [RequestFormLimits(ValueCountLimit = int.MaxValue)]
    [Authorize(Roles = "SuperAdmin, Admin, Teacher")]
    public class EditModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public int ExamId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int QuestionId { get; set; }

        [BindProperty]
        public CreateExamInputModel Settings { get; set; } = new();
        public Question? Question { get; set; }

        private readonly IExamEditFlowService _examEditFlowService;
        private readonly IExamEditMutationService _examEditMutationService;
        private readonly IExamService _examService;
        private readonly ApplicationDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public ExaminationHeaderModel Header { get; private set; } = new ExaminationHeaderModel
        {
            ExamTitle = "Exam Editor",
            CourseName = string.Empty,
            EndDateTime = null
        };

        public PaginationModel Pagination { get; private set; } = new PaginationModel();
        public bool UploadsEnabled { get; private set; }

        public EditModel(
            IExamEditFlowService examEditFlowService,
            IExamEditMutationService examEditMutationService,
            IExamService examService,
            ApplicationDbContext dbContext,
            IConfiguration configuration)
        {
            _examEditFlowService = examEditFlowService;
            _examEditMutationService = examEditMutationService;
            _examService = examService;
            _dbContext = dbContext;
            _configuration = configuration;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await CanManageExamAsync(ExamId))
                return Forbid();

            var pageData = await _examEditFlowService.LoadPageAsync(ExamId, QuestionId, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (pageData == null)
                return NotFound();

            if (pageData.RedirectToCanonicalQuestion)
                return RedirectToPage(new { examId = ExamId, questionId = pageData.ResolvedQuestionId });

            QuestionId = pageData.ResolvedQuestionId;
            Header = pageData.Header;
            Pagination = pageData.Pagination;
            Settings = pageData.Settings;
            Question = pageData.Question;
            UploadsEnabled = pageData.UploadsEnabled;
            ViewData["StoragePrefix"] = pageData.StoragePrefix;
            ViewData["UploadsEnabled"] = pageData.UploadsEnabled;

            return Page();
        }

        // Loads existing saved question shell
        public async Task<IActionResult> OnGetQuestionShellAsync(int examId, int questionId)
        {
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var shell = await _examEditFlowService.LoadQuestionShellAsync(examId, questionId, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (shell == null) return NotFound();

            var provider = (_configuration["Storage:Provider"] ?? "").Trim();
            ViewData["StoragePrefix"] = _configuration["Storage:Prefix"] ?? "dev";
            ViewData["UploadsEnabled"] = !provider.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

            return Partial("/Pages/Shared/Examination/Questions/_QuestionShell.cshtml", shell);
        }
        // Returns a server-rendered draft shell (no DB, QuestionId = 0)
        public IActionResult OnGetQuestionShellDraft(int examId, string kind, int questionNumber)
        {
            if (examId <= 0) return NotFound();
            if (questionNumber <= 0) return BadRequest("Invalid questionNumber.");
            if (string.IsNullOrWhiteSpace(kind)) return BadRequest("Missing kind.");

            var provider = (_configuration["Storage:Provider"] ?? "").Trim();
            ViewData["StoragePrefix"] = _configuration["Storage:Prefix"] ?? "dev";
            ViewData["UploadsEnabled"] = !provider.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

            var shell = _examEditFlowService.BuildDraftQuestionShell(kind, questionNumber);
            return Partial("/Pages/Shared/Examination/Questions/_QuestionShell.cshtml", shell);
        }

        // Single upsert (create if QuestionId==0, update otherwise)
        public async Task<IActionResult> OnPostSaveQuestionAsync(
            int examId,
            int questionId,
            string kind,
            int questionNumber,
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
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var result = await _examEditMutationService.SaveQuestionAsync(new SaveQuestionCommand
            {
                ExamId = examId,
                QuestionId = questionId,
                Kind = kind,
                QuestionNumber = questionNumber,
                Text = text,
                MessageBeforeQuestion = messageBeforeQuestion,
                Scenario = scenario,
                Feedback = feedback,
                FunFact = funFact,
                Score = score,
                OpenCorrectAnswerText = openCorrectAnswerText,
                MultipleChoiceAnswerA = multipleChoiceAnswerA,
                MultipleChoiceAnswerB = multipleChoiceAnswerB,
                MultipleChoiceAnswerC = multipleChoiceAnswerC,
                MultipleChoiceAnswerD = multipleChoiceAnswerD,
                MultipleChoiceCorrect = !string.IsNullOrWhiteSpace(multipleChoiceCorrect)
                    ? multipleChoiceCorrect
                    : (Request.Form["multipleChoiceCorrect"].FirstOrDefault()
                        ?? Request.Form["MultipleChoiceCorrect"].FirstOrDefault()),
                TrueOrFalseCorrect = !string.IsNullOrWhiteSpace(trueOrFalseCorrect)
                    ? trueOrFalseCorrect
                    : (Request.Form["trueOrFalseCorrect"].FirstOrDefault()
                        ?? Request.Form["TrueOrFalseCorrect"].FirstOrDefault()
                        ?? Request.Form["tfCorrect"].FirstOrDefault()),
                ImageReference = imageReference,
                AudioReference = audioReference
            }, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));

            if (!result.Success)
                return StatusCode(result.StatusCode, new { success = false, errorMessage = result.ErrorMessage });

            return new JsonResult(new { success = true, questionId = result.QuestionId, questionNumber = result.QuestionNumber });
        }

        public async Task<IActionResult> OnPostSaveSettingsAsync()
        {
            if (ExamId <= 0)
                return NotFound();
            if (!await CanManageExamAsync(ExamId))
                return Forbid();

            if (!ModelState.IsValid)
            {
                // Re-load tag list + header/pagination needed to render the page again
                // (same as in OnGetAsync) then:
                await OnGetAsync();
                return Page();
            }

            var result = await _examEditMutationService.SaveSettingsAsync(ExamId, Settings, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (!result.Success)
                return StatusCode(result.StatusCode);

            return RedirectToPage(new { ExamId, QuestionId });
        }

        public async Task<IActionResult> OnPostSaveQuickSettingsAsync()
        {
            if (ExamId <= 0)
                return NotFound();
            if (!await CanManageExamAsync(ExamId))
                return Forbid();

            var exam = await _examService.GetExamForSettingsUpdateAsync(ExamId);
            if (exam == null)
                return NotFound();

            exam.DifficultyValue = Settings.DifficultyValue ?? exam.DifficultyValue;
            exam.MaxAttempts = Settings.MaxAttempts is >= 1 ? Settings.MaxAttempts.Value : -1;

            await _examService.SaveChangesAsync();

            if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new
                {
                    success = true,
                    difficultyValue = exam.DifficultyValue,
                    maxAttempts = exam.MaxAttempts
                });
            }

            return RedirectToPage(new { ExamId, QuestionId });
        }

        public async Task<IActionResult> OnPostSaveHeaderSettingsAsync(int examId, string field, string? name, int? timeLimit)
        {
            if (examId <= 0)
                return BadRequest(new { success = false, errorMessage = "Invalid exam." });
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var exam = await _examService.GetExamForSettingsUpdateAsync(examId);
            if (exam == null)
                return NotFound(new { success = false, errorMessage = "Exam not found." });

            if (string.Equals(field, "name", StringComparison.OrdinalIgnoreCase))
            {
                exam.Name = await ResolveExamNameAsync(name, examId);
            }
            else if (string.Equals(field, "timeLimit", StringComparison.OrdinalIgnoreCase))
            {
                exam.TimeLimit = timeLimit is >= 1 ? timeLimit.Value : null;
            }
            else
            {
                return BadRequest(new { success = false, errorMessage = "Unknown field." });
            }

            await _examService.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                name = exam.Name,
                timeLimit = exam.TimeLimit,
                timeLimitDisplay = exam.TimeLimit.HasValue ? TimeSpan.FromMinutes(exam.TimeLimit.Value).ToString(@"hh\:mm\:ss") : "No time limit"
            });
        }

        public async Task<IActionResult> OnPostCreateImageUploadAsync(
            int examId,
            int questionId,
            string fileName,
            string? contentType)
        {
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var result = await _examEditMutationService.CreateUploadAsync(examId, questionId, fileName, contentType, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (!result.Success)
            {
                return StatusCode(result.StatusCode, new
                {
                    success = false,
                    errorMessage = result.ErrorMessage,
                    errorCode = result.ErrorCode
                });
            }

            return new JsonResult(new { success = true, objectKey = result.ObjectKey, putUrl = result.PutUrl });
        }

        public async Task<IActionResult> OnPostDeleteQuestionFileAsync(int examId, int questionId, string kind)
        {
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var result = await _examEditMutationService.DeleteQuestionFileAsync(examId, questionId, kind, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (!result.Success)
                return StatusCode(result.StatusCode, new { success = false, errorMessage = result.ErrorMessage });

            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostDeleteQuestionAsync(int examId, int questionId)
        {
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var result = await _examEditMutationService.DeleteQuestionAsync(examId, questionId, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (!result.Success)
                return StatusCode(result.StatusCode, new { success = false, errorMessage = result.ErrorMessage });

            return new JsonResult(new { success = true, nextQuestionId = result.NextQuestionId });
        }

        public async Task<IActionResult> OnGetPaginationAsync(int examId, int currentQuestionId)
        {
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var model = await _examEditFlowService.LoadPaginationAsync(examId, currentQuestionId, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (model == null) return NotFound();

            return Partial("/Pages/Shared/Examination/_Pagination.cshtml", model);
        }

        public async Task<IActionResult> OnPostPublishExamAsync(int examId, bool publishWithWarnings = false)
        {
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var result = await _examEditMutationService.PublishExamAsync(examId, publishWithWarnings, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (!result.Success)
                return StatusCode(result.StatusCode, new { success = false, errorMessage = result.ErrorMessage });

            return new JsonResult(new
            {
                success = true,
                published = result.Published,
                canPublish = result.CanPublish,
                requiresWarningConfirmation = result.RequiresWarningConfirmation,
                message = result.Message,
                errors = result.Errors,
                warnings = result.Warnings
            });
        }


        public async Task<IActionResult> OnPostUnpublishExamAsync(int examId)
        {
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var result = await _examEditMutationService.UnpublishExamAsync(examId, CurrentUserId, (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")));
            if (!result.Success)
                return StatusCode(result.StatusCode, new { success = false, errorMessage = result.ErrorMessage });

            return new JsonResult(new { success = true, unpublished = true, message = "Exam unpublished successfully." });
        }

        private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

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

        private async Task<string> ResolveExamNameAsync(string? requestedName, int examId)
        {
            var baseName = string.IsNullOrWhiteSpace(requestedName)
                ? "untitled-exam"
                : requestedName.Trim();

            if (!baseName.Equals("untitled-exam", StringComparison.OrdinalIgnoreCase))
                return baseName;

            var existingNames = await _dbContext.Exams
                .AsNoTracking()
                .Where(e => e.Id != examId && !e.IsDeleted && (e.Name == baseName || EF.Functions.Like(e.Name, baseName + "-%")))
                .Select(e => e.Name)
                .ToListAsync();

            if (!existingNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
                return baseName;

            for (var index = 2; index < 10000; index++)
            {
                var candidate = $"{baseName}-{index}";
                if (!existingNames.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    return candidate;
            }

            return $"{baseName}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

    }
}
