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
        int? TimeLimit,
        string? PublicSlug);

    public interface IExamVersionService
    {
        Task<PublishedExamDescriptor?> GetPublishedDescriptorAsync(int examId, int? examVersionId = null);
        Task<List<QuestionMetadata>> GetQuestionMetadataAsync(int examId, int? examVersionId = null);
        Task<Question?> GetQuestionForTakeAsync(int examId, int questionId, int? examVersionId = null);
        Task<Exam?> GetExamForEvaluationAsync(int examId, int? examVersionId = null);
        Task<OperationResult> PublishVersionAsync(Exam exam, IReadOnlyCollection<Question> questions);
    }
}
