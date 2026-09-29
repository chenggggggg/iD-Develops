namespace iD_Develops.Services
{
    public interface IExamAttemptService
    {
        Task<AttemptState> GetStateAsync(string userId, int examId);

        /// <summary>
        /// Returns the record to navigate to after pressing Start.
        /// Either creates a new attempt or returns an existing in-progress attempt (depending on policy).
        /// </summary>
        Task<AttemptActionResult> StartAsync(string userId, int examId);

        Task<AttemptActionResult> ContinueAsync(string userId, int examId);

        /// <summary>
        /// Cancels the in-progress attempt (if any) and starts a new attempt (if allowed).
        /// </summary>
        Task<AttemptActionResult> RestartAsync(string userId, int examId);
    }

    public sealed record AttemptState(
        int ExamId,
        int MaxAttempts,              // -1 = unlimited, >=1 fixed
        bool HasUnlimitedAttempts,
        int AttemptsUsed,
        int AttemptsLeft,             // int.MaxValue if unlimited
        Guid? InProgressRecordId,
        bool CanStart,
        bool CanContinue,
        bool CanRestart,
        string? BlockReason           // e.g. "No attempts left"
    );

    public sealed record AttemptActionResult(
        bool Success,
        Guid? RecordId,
        string? ErrorMessage
    );

}
