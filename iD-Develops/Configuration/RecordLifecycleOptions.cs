namespace iD_Develops.Configuration
{
    public sealed class RecordLifecycleOptions
    {
        public int SubmissionGraceSeconds { get; set; } = 30;
        public bool EnableNoTimeLimitAbandonmentCleanup { get; set; } = true;
        public int NoTimeLimitInactivityDays { get; set; } = 30;
    }
}
