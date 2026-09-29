using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace iD_Develops.Pages.Portal.Exams
{
    [Authorize(Policy = "ManageExams")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumFileSize)]
    [RequestSizeLimit(MaximumFileSize)]
    public sealed class TransferModel : PageModel
    {
        private const long MaximumFileSize = 5 * 1024 * 1024;

        private readonly IExamTransferService _transferService;
        private readonly IExamService _examService;
        private readonly ILogger<TransferModel> _logger;
        private readonly IWebHostEnvironment _environment;

        public TransferModel(
            IExamTransferService transferService,
            IExamService examService,
            ILogger<TransferModel> logger,
            IWebHostEnvironment environment)
        {
            _transferService = transferService;
            _examService = examService;
            _logger = logger;
            _environment = environment;
        }

        [BindProperty]
        public IFormFile? ExamFile { get; set; }

        [TempData]
        public string? TransferSuccessMessage { get; set; }

        [TempData]
        public string? TransferWarningMessage { get; set; }

        public int? ImportedExamId { get; private set; }

        public ImportFailureDiagnostics? ImportFailure { get; private set; }

        public void OnGet(int? importedExamId = null)
        {
            ImportedExamId = importedExamId is > 0 ? importedExamId : null;
        }

        public async Task<IActionResult> OnGetExportAsync(
            int examId,
            CancellationToken cancellationToken)
        {
            if (!await CanManageExamAsync(examId))
                return Forbid();

            var export = await _transferService.ExportAsync(examId, cancellationToken);
            if (export == null)
                return NotFound();

            return File(export.Content, "application/json", export.FileName);
        }

        public async Task<IActionResult> OnPostImportAsync(CancellationToken cancellationToken)
        {
            if (ExamFile == null || ExamFile.Length == 0)
            {
                return ShowImportFailure(
                    "Checking uploaded file",
                    "Select an exam JSON file to import.");
            }

            if (ExamFile.Length > MaximumFileSize)
            {
                return ShowImportFailure(
                    "Checking uploaded file",
                    "The exam file must be 5 MB or smaller.",
                    [$"Uploaded size: {ExamFile.Length:N0} bytes.", $"Maximum size: {MaximumFileSize:N0} bytes."]);
            }

            if (!string.Equals(Path.GetExtension(ExamFile.FileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                return ShowImportFailure(
                    "Checking uploaded file",
                    "Select a .json exam export file.",
                    [$"Uploaded file name: {ExamFile.FileName}"]);
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            try
            {
                await using var stream = ExamFile.OpenReadStream();
                var result = await _transferService.ImportAsync(stream, userId, cancellationToken);

                if (!result.Success || !result.ExamId.HasValue)
                {
                    return ShowImportFailure(
                        result.FailureStage ?? "Processing import",
                        result.ErrorMessage ?? "The exam could not be imported.",
                        result.DiagnosticDetails);
                }

                TransferSuccessMessage = "The exam was imported as a new draft. Review it before publishing.";
                if (result.Warnings is { Count: > 0 })
                    TransferWarningMessage = string.Join(" ", result.Warnings);

                return RedirectToPage(new { importedExamId = result.ExamId.Value });
            }
            catch (Exception ex)
            {
                var stage = ex is ExamTransferImportException importException
                    ? importException.Stage
                    : "Processing import";
                var summary = ex is ExamTransferImportException
                    ? ex.Message
                    : "The exam could not be imported. No exam was created.";

                return ShowImportFailure(stage, summary, exception: ex);
            }
        }

        private IActionResult ShowImportFailure(
            string stage,
            string summary,
            IReadOnlyList<string>? diagnosticDetails = null,
            Exception? exception = null)
        {
            var referenceId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            var details = diagnosticDetails?.Where(detail => !string.IsNullOrWhiteSpace(detail)).ToList()
                ?? new List<string>();

            for (var currentException = exception; currentException != null; currentException = currentException.InnerException)
                details.Add($"{currentException.GetType().Name}: {currentException.Message}");

            if (exception == null)
            {
                _logger.LogWarning(
                    "Exam import failed [{ReferenceId}] at stage {Stage} for file {FileName}. {Summary} Diagnostics: {Diagnostics}",
                    referenceId,
                    stage,
                    ExamFile?.FileName,
                    summary,
                    details);
            }
            else
            {
                _logger.LogError(
                    exception,
                    "Exam import failed [{ReferenceId}] at stage {Stage} for file {FileName}.",
                    referenceId,
                    stage,
                    ExamFile?.FileName);
            }

            var showTechnicalDetails = _environment.IsDevelopment()
                || _environment.IsStaging()
                || _environment.IsEnvironment("Local");

            ImportFailure = new ImportFailureDiagnostics(
                referenceId,
                stage,
                summary,
                ExamFile?.FileName,
                showTechnicalDetails,
                details);

            ModelState.AddModelError(nameof(ExamFile), summary);
            return Page();
        }

        private async Task<bool> CanManageExamAsync(int examId)
        {
            if (examId <= 0)
                return false;

            if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
                return true;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return User.IsInRole("Teacher")
                && !string.IsNullOrWhiteSpace(userId)
                && await _examService.CanTeacherManageExamAsync(examId, userId);
        }
    }

    public sealed record ImportFailureDiagnostics(
        string ReferenceId,
        string Stage,
        string Summary,
        string? FileName,
        bool ShowTechnicalDetails,
        IReadOnlyList<string> TechnicalDetails);
}
