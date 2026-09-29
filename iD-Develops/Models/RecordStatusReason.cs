namespace iD_Develops.Models
{
    public enum RecordStatusReason
    {
        None = 0,
        Started = 1,
        ManualSubmit = 2,
        DeadlinePassed = 3,
        RestartedByUser = 4,
        DuplicateInProgressCleanup = 5,
        AbandonedNoTimeLimit = 6
    }
}
