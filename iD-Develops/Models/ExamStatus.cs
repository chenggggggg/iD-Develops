namespace iD_Develops.Models
{
    public enum ExamStatus
    {
        // Record has not started yet (reserved for future use).
        NotStarted,
        // Active attempt, timer is still running.
        InProgress,
        // Submitted within the allowed time window.
        Completed,
        // Time limit has passed before a valid submit.
        Overdue,
        // Attempt was intentionally stopped (restart/manual cancel/cleanup).
        Cancelled,
        // Record could not be found or state is not resolvable.
        Invalid,
        // Legacy value kept for backward compatibility with existing data.
        Disconnected
    }
}
