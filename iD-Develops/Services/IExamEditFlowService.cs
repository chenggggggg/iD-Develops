using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Pages.Shared.Examination;
using iD_Develops.Pages.Shared.Examination.Questions;

namespace iD_Develops.Services
{
    public sealed class ExamEditorPageData
    {
        public ExaminationHeaderModel Header { get; init; } = new();
        public PaginationModel Pagination { get; init; } = new();
        public CreateExamInputModel Settings { get; init; } = new();
        public Question? Question { get; init; }
        public int ResolvedQuestionId { get; init; }
        public bool RedirectToCanonicalQuestion { get; init; }
        public bool UploadsEnabled { get; init; }
        public string StoragePrefix { get; init; } = "dev";
    }

    public interface IExamEditFlowService
    {
        Task<ExamEditorPageData?> LoadPageAsync(int examId, int questionId, string? userId, bool isAdmin);
        Task<QuestionShellModel?> LoadQuestionShellAsync(int examId, int questionId, string? userId, bool isAdmin);
        QuestionShellModel BuildDraftQuestionShell(string kind, int questionNumber);
        Task<PaginationModel?> LoadPaginationAsync(int examId, int currentQuestionId, string? userId, bool isAdmin);
    }
}
