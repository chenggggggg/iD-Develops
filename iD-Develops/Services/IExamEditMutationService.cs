using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed class SaveQuestionCommand
    {
        public int ExamId { get; init; }
        public int QuestionId { get; init; }
        public string Kind { get; init; } = string.Empty;
        public int QuestionNumber { get; init; }
        public string? Text { get; init; }
        public string? MessageBeforeQuestion { get; init; }
        public string? Scenario { get; init; }
        public string? Feedback { get; init; }
        public string? FunFact { get; init; }
        public double? Score { get; init; }
        public string? OpenCorrectAnswerText { get; init; }
        public string? MultipleChoiceAnswerA { get; init; }
        public string? MultipleChoiceAnswerB { get; init; }
        public string? MultipleChoiceAnswerC { get; init; }
        public string? MultipleChoiceAnswerD { get; init; }
        public string? MultipleChoiceCorrect { get; init; }
        public string? TrueOrFalseCorrect { get; init; }
        public string? ImageReference { get; init; }
        public string? AudioReference { get; init; }
    }

    public sealed class SaveQuestionResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public int StatusCode { get; init; }
        public int QuestionId { get; init; }
        public int QuestionNumber { get; init; }
    }

    public sealed class SettingsMutationResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public int StatusCode { get; init; }
    }

    public sealed class PresignedUploadResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? ErrorCode { get; init; }
        public int StatusCode { get; init; }
        public string? ObjectKey { get; init; }
        public string? PutUrl { get; init; }
    }

    public sealed class DeleteQuestionResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public int StatusCode { get; init; }
        public int NextQuestionId { get; init; }
    }

    public sealed class PublishExamResult
    {
        public bool Success { get; init; }
        public bool Published { get; init; }
        public bool CanPublish { get; init; }
        public bool RequiresWarningConfirmation { get; init; }
        public string? ErrorMessage { get; init; }
        public string? Message { get; init; }
        public int StatusCode { get; init; }
        public List<object> Errors { get; init; } = new();
        public List<object> Warnings { get; init; } = new();
    }

    public interface IExamEditMutationService
    {
        Task<SaveQuestionResult> SaveQuestionAsync(SaveQuestionCommand command, string? userId, bool isAdmin);
        Task<SettingsMutationResult> SaveSettingsAsync(int examId, CreateExamInputModel settings, string? userId, bool isAdmin);
        Task<PresignedUploadResult> CreateUploadAsync(int examId, int questionId, string fileName, string? contentType, string? userId, bool isAdmin);
        Task<SettingsMutationResult> DeleteQuestionFileAsync(int examId, int questionId, string kind, string? userId, bool isAdmin);
        Task<DeleteQuestionResult> DeleteQuestionAsync(int examId, int questionId, string? userId, bool isAdmin);
        Task<PublishExamResult> PublishExamAsync(int examId, bool publishWithWarnings, string? userId, bool isAdmin);
        Task<SettingsMutationResult> UnpublishExamAsync(int examId, string? userId, bool isAdmin);
    }
}
