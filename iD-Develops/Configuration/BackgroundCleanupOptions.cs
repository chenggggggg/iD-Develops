namespace iD_Develops.Configuration;

public sealed class BackgroundCleanupOptions
{
    public const string SectionName = "BackgroundCleanup";

    public int UnconfirmedUserIntervalHours { get; set; } = 24;

    public int UnconfirmedUserRetentionDays { get; set; } = 7;

    public int UnconfirmedUserBatchSize { get; set; } = 100;

    public int InviteReservationIntervalMinutes { get; set; } = 5;
}
