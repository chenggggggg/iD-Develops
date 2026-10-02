namespace iD_Develops.Pages.Portal.Home
{
    public sealed record DashboardMetric(string Label, string Value, string Icon, string Tone);

    public sealed record DashboardEventsModel(
        string Title,
        IReadOnlyList<DashboardEvent> Events,
        bool ShowStatus);
}
