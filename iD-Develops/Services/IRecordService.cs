using iD_Develops.Models;

namespace iD_Develops.Services
{
    public sealed record UserExamRecordSummary(
        Guid RecordId,
        int ExamId,
        string ExamName,
        int? ExamVersionNumber,
        DateTime StartDateTime,
        DateTime? EndDateTime,
        ExamStatus ExamStatus,
        double Score,
        double MaxScore);

    public interface IRecordService
    {
        Task<AttemptAccessState> GetAttemptAccessStateAsync(Guid recordId, int? examId = null, string? userId = null, CancellationToken cancellationToken = default);
        Task<Guid> CreateRecordAsync(Record record);
        Task CompleteRecordAsync(Guid id, DateTime submitTime);
        Task<int> MarkExpiredRecordsOverdueAsync(CancellationToken cancellationToken = default);
        Task<ExamStatus> GetExamStatusAsync(Guid recordId, int? examId = null, CancellationToken cancellationToken = default);
        Task<DateTime?> GetRecordEndTimeAsync(Guid recordId);
        Task<int> CountAttemptsAsync(string userId, int examId, int? examVersionId = null);
        Task<bool> CancelInProgressAsync(Guid recordId, string userId, int examId, int? examVersionId = null);
        Task<List<Record>> GetInProgressRecordsAsync(string userId, int examId, int? examVersionId = null);

        /// <summary>
        /// Cancels (sets ExamStatus=Cancelled) multiple records by id.
        /// Intended for self-healing duplicate InProgress attempts.
        /// Must only cancel records that belong to the user & exam.
        /// </summary>
        Task<int> CancelRecordsAsync(IEnumerable<Guid> recordIds, string userId, int examId, int? examVersionId = null);

        /// <summary>
        /// Starts an attempt in an atomic manner:
        /// - If an InProgress record exists, returns its Id.
        /// - Otherwise, creates a new Record and returns its Id.
        /// Includes optional self-heal: if multiple InProgress exist, keeps the latest and cancels the rest.
        /// </summary>
        Task<Guid> StartAttemptAtomicAsync(string userId, int examId, int? examVersionId, DateTime startTimeUtc, DateTime? endTimeUtc);
        Task<IReadOnlyList<UserExamRecordSummary>> GetUserResultRecordsAsync(string userId, CancellationToken cancellationToken = default);
    }
}
