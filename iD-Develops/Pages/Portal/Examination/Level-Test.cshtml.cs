using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Pages.Shared.Examination;
using iD_Develops.Pages.Shared.Examination.Questions;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Examination
{
    [ValidateAntiForgeryToken]
    public class LevelTestModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public int ExamId { get; set; }

        [BindProperty(SupportsGet = true)]
        public Guid RecordId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int QuestionId { get; set; }

        public ExamPageState PageState { get; private set; } = default!;
        public PaginationModel Pagination { get; private set; } = new PaginationModel();

        private readonly IExamTakeFlowService _examTakeFlowService;
        private readonly IConfiguration _configuration;

        public LevelTestModel(
            IExamTakeFlowService examTakeFlowService,
            IConfiguration configuration)
        {
            _examTakeFlowService = examTakeFlowService;
            _configuration = configuration;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var pageData = await _examTakeFlowService.LoadPageAsync(
                ExamId > 0 ? ExamId : null,
                RecordId,
                QuestionId);

            if (!pageData.Exists)
                return NotFound();

            if (pageData.ExamStatus == ExamStatus.Completed || pageData.ExamStatus == ExamStatus.Overdue)
                return RedirectToPage("/Portal/Examination/Completed", new { recordId = RecordId });

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

            Pagination = new PaginationModel
            {
                QuestionsMetadata = pageData.QuestionsMetadata,
                CurrentQuestionId = QuestionId,
                Mode = PaginationMode.Exam
            };

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

        public async Task<IActionResult> OnGetQuestionShellAsync(int? examId, int questionId, Guid recordId)
        {
            if (questionId <= 0 || recordId == Guid.Empty)
                return BadRequest();

            var questionData = await _examTakeFlowService.LoadQuestionAsync(examId, recordId, questionId);
            if (!questionData.Exists)
                return NotFound();

            if (questionData.ExamStatus != ExamStatus.InProgress)
            {
                return StatusCode(StatusCodes.Status409Conflict,
                    new { success = false, message = "Exam attempt is no longer in progress." });
            }

            var question = questionData.Question;
            if (question == null)
                return NotFound();

            ViewData["StoragePrefix"] = _configuration["Storage:Prefix"] ?? "dev";

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
            return StatusCode(result.StatusCode, new { success = result.Success, message = result.ErrorMessage });
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
                recordId => Url.Page("/Portal/Examination/Completed", new { recordId }));

            return new JsonResult(new
            {
                success = result.Success,
                errorMessage = result.ErrorMessage,
                redirectUrl = result.RedirectUrl
            });
        }
    }
}
