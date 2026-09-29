using iD_Develops.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed class ExamTakePageData
    {
        public bool Exists { get; init; }
        public bool UserMatches { get; init; } = true;
        public int ExamId { get; init; }
        public int QuestionId { get; init; }
        public int? ExamVersionId { get; init; }
        public ExamStatus ExamStatus { get; init; }
        public DateTime? EndDateTime { get; init; }
        public Question? CurrentQuestion { get; init; }
        public string? CurrentSavedAnswerText { get; init; }
        public List<QuestionMetadata> QuestionsMetadata { get; init; } = new();
        public PublishedExamDescriptor? Descriptor { get; init; }
    }

    public sealed class ExamTakeQuestionData
    {
        public bool Exists { get; init; }
        public bool UserMatches { get; init; } = true;
        public int ExamId { get; init; }
        public int QuestionId { get; init; }
        public ExamStatus ExamStatus { get; init; }
        public Question? Question { get; init; }
        public string? SavedAnswerText { get; init; }
    }

    public sealed class ExamTakeAnswerSaveResult
    {
        public bool Success { get; init; }
        public int StatusCode { get; init; }
        public string? ErrorMessage { get; init; }
        public int? ExamId { get; init; }
        public int QuestionId { get; init; }
        public Guid RecordId { get; init; }
        public string AnswerText { get; init; } = string.Empty;
        public string Source { get; init; } = "manual";
        public bool SaveRejected { get; init; }
    }

    public sealed class ExamTakeValidationResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public List<int> MissingQuestionNumbers { get; init; } = new();
    }

    public sealed class ExamTakeSubmitResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? RedirectUrl { get; init; }
    }

    public interface IExamTakeFlowService
    {
        Task<ExamTakePageData> LoadPageAsync(int? examId, Guid recordId, int questionId, string? userId = null, bool requireOwnership = false);
        Task<ExamTakeQuestionData> LoadQuestionAsync(int? examId, Guid recordId, int questionId, string? userId = null, bool requireOwnership = false);
        Task<ExamTakeAnswerSaveResult> SaveAnswerAsync(IFormCollection formData);
        Task<ExamTakeValidationResult> ValidateMissingQuestionsAsync(int? examId, Guid recordId);
        Task<ExamTakeSubmitResult> SubmitExamAsync(IFormCollection formData, Func<Guid, string?> buildRedirectUrl);
    }
}
