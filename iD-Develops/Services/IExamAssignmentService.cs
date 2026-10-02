using iD_Develops.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record ExamAssignmentUserOption(string UserId, string Name, string Email);

    public sealed record ExamAssignmentItem(
        string UserId,
        string Name,
        string Email,
        DateTime AssignedAtUtc,
        DateTime? UnlockAtUtc,
        DateTime? DueAtUtc,
        string AssignedBy);

    public sealed record ExamAttemptGrantItem(
        int Id,
        string UserId,
        string UserName,
        int AdditionalAttempts,
        DateTime GrantedAtUtc,
        string GrantedBy,
        string? Reason);

    public sealed record ExamAssignmentPageData(
        int ExamId,
        string ExamName,
        ExamPublishStatus PublishStatus,
        IReadOnlyList<ExamAssignmentItem> Assignments,
        IReadOnlyList<ExamAssignmentUserOption> UserOptions,
        IReadOnlyList<ExamAttemptGrantItem> AttemptGrants);

    public interface IExamAssignmentService
    {
        Task<ExamAssignmentPageData?> GetPageAsync(int examId, string actorUserId, bool canManageAll, CancellationToken cancellationToken = default);
        Task<OperationResult> AssignAsync(int examId, string userId, DateTime? unlockAtUtc, DateTime? dueAtUtc, string actorUserId, bool canManageAll, CancellationToken cancellationToken = default);
        Task<OperationResult> RemoveAsync(int examId, string userId, string actorUserId, bool canManageAll, CancellationToken cancellationToken = default);
        Task<OperationResult> GrantAttemptsAsync(int examId, string userId, int additionalAttempts, string? reason, string actorUserId, bool canManageAll, CancellationToken cancellationToken = default);
    }
}
