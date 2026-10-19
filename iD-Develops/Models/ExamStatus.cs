namespace iD_Develops.Models
{
    public enum ExamStatus
    {
        // Record has not started yet (reserved for future use).
        NotStarted = 0,
        // Active attempt, timer is still running.
        InProgress = 1,
        // Submitted manually or finalized automatically when the time limit expires.
        Completed = 2,
        // Attempt was intentionally stopped (restart/manual cancel/cleanup).
        Cancelled = 4,
        // Record could not be found or state is not resolvable.
        Invalid = 5,
        // Legacy value kept for backward compatibility with existing data.
        Disconnected = 6
    }
}
