namespace iD_Develops.Configuration;

public sealed class DependencyRecoveryOptions
{
    public const string SectionName = "DependencyRecovery";

    public int InitialRetrySeconds { get; set; } = 5;

    public int MaximumRetrySeconds { get; set; } = 60;

    public int DatabaseProbeIntervalSeconds { get; set; } = 30;

    public int DatabaseAttemptTimeoutSeconds { get; set; } = 15;

    public int DatabaseConnectionTimeoutSeconds { get; set; } = 5;

    public int DatabaseRequestRetryCount { get; set; } = 2;

    public int DatabaseRequestMaximumDelaySeconds { get; set; } = 2;

    public int StripeProbeIntervalSeconds { get; set; } = 300;

    public int StripeAttemptTimeoutSeconds { get; set; } = 15;
}
