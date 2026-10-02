namespace iD_Develops.Services
{
    public sealed record ExamAccessDecision(
        bool CanTake,
        DateTime? UnlockAtUtc,
        DateTime? DueAtUtc,
        string? BlockReason);

    public interface IExamAccessService
    {
        Task<bool> CanTakeExamAsync(
            string userId,
            int examId,
            CancellationToken cancellationToken = default);

        async Task<ExamAccessDecision> GetAccessDecisionAsync(
            string userId,
            int examId,
            CancellationToken cancellationToken = default)
        {
            var canTake = await CanTakeExamAsync(userId, examId, cancellationToken);
            return new ExamAccessDecision(
                canTake,
                null,
                null,
                canTake ? null : "This exam is not assigned to your account.");
        }
    }
}
