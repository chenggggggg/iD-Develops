using iD_Develops.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record PublishedExamDescriptor(
        int ExamId,
        int? ExamVersionId,
        string ExamTitle,
        string CourseName,
        int DurationSeconds,
        string IntroductionPrimary,
        string? IntroductionSecondary,
        int MaxAttempts,
        string? PublicSlug,
        int? VersionNumber = null);

    public interface IExamVersionService
    {
        Task<PublishedExamDescriptor?> GetPublishedDescriptorAsync(int examId, int? examVersionId = null);
        Task<PublishedExamDescriptor?> GetOrCreateAttemptVersionAsync(int examId);
        Task<ExamVersion?> GetVersionAsync(int examId, int versionId);
        Task<bool> IsMediaReferencedAsync(int examId, string reference);
        Task<IReadOnlyList<ExamVersionSummary>> GetVersionHistoryAsync(IReadOnlyCollection<int> examIds);
        Task<List<QuestionMetadata>> GetQuestionMetadataAsync(int examId, int? examVersionId = null);
        Task<Question?> GetQuestionForTakeAsync(int examId, int questionId, int? examVersionId = null);
        Task<Exam?> GetExamForEvaluationAsync(int examId, int? examVersionId = null);
    }

    public sealed record ExamVersionSummary(int ExamId, int VersionId, string ExamName, int VersionNumber,
        DateTime CreatedAtUtc, int? TimeLimitSeconds, int QuestionCount, int AttemptCount);
}
