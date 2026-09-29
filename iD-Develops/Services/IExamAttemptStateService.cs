namespace iD_Develops.Services
{
    public interface IExamAttemptStateService
    {
        Task<ExamAttemptPageLoadResult> LoadPageAsync(int? examId, Guid recordId, int requestedQuestionId, string? userId = null, bool requireOwnership = false, CancellationToken cancellationToken = default);
        Task<ExamAttemptQuestionStateResult> LoadQuestionAsync(int? examId, Guid recordId, int questionId, string? userId = null, bool requireOwnership = false, CancellationToken cancellationToken = default);
    }
}
