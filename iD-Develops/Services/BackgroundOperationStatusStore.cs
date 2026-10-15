namespace iD_Develops.Services;

public enum BackgroundOperationState
{
    Healthy,
    Waiting,
    Failed
}

public sealed record BackgroundOperationSnapshot(
    string Key,
    string DisplayName,
    BackgroundOperationState State,
    DateTime? LastStartedAtUtc,
    DateTime? LastCompletedAtUtc,
    DateTime? NextRunAtUtc,
    int? AffectedCount,
    string Message);

public sealed record BackgroundOperationEvent(
    DateTime OccurredAtUtc,
    string OperationName,
    BackgroundOperationState State,
    string Message);

public interface IBackgroundOperationStatusStore
{
    IReadOnlyList<BackgroundOperationSnapshot> GetOperations();

    IReadOnlyList<BackgroundOperationEvent> GetRecentEvents();

    void ReportSuccess(
        string key,
        string displayName,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        DateTime nextRunAtUtc,
        int affectedCount,
        string message);

    void ReportWaiting(string key, string displayName, DateTime nextRunAtUtc, string message);

    void ReportFailure(string key, string displayName, DateTime nextRunAtUtc, string message);
}

public sealed class BackgroundOperationStatusStore : IBackgroundOperationStatusStore
{
    private const int EventLimit = 50;
    private readonly object _sync = new();
    private readonly Dictionary<string, BackgroundOperationSnapshot> _operations = new(StringComparer.Ordinal);
    private readonly List<BackgroundOperationEvent> _events = [];

    public IReadOnlyList<BackgroundOperationSnapshot> GetOperations()
    {
        lock (_sync)
        {
            return _operations.Values
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<BackgroundOperationEvent> GetRecentEvents()
    {
        lock (_sync)
        {
            return _events.ToList();
        }
    }

    public void ReportSuccess(
        string key,
        string displayName,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        DateTime nextRunAtUtc,
        int affectedCount,
        string message)
    {
        lock (_sync)
        {
            _operations[key] = new(
                key,
                displayName,
                BackgroundOperationState.Healthy,
                startedAtUtc,
                completedAtUtc,
                nextRunAtUtc,
                affectedCount,
                message);

            if (affectedCount > 0)
            {
                AddEvent(new(completedAtUtc, displayName, BackgroundOperationState.Healthy, message));
            }
        }
    }

    public void ReportWaiting(string key, string displayName, DateTime nextRunAtUtc, string message)
    {
        lock (_sync)
        {
            _operations.TryGetValue(key, out var previous);
            _operations[key] = new(
                key,
                displayName,
                BackgroundOperationState.Waiting,
                previous?.LastStartedAtUtc,
                previous?.LastCompletedAtUtc,
                nextRunAtUtc,
                previous?.AffectedCount,
                message);
            AddEvent(new(DateTime.UtcNow, displayName, BackgroundOperationState.Waiting, message));
        }
    }

    public void ReportFailure(string key, string displayName, DateTime nextRunAtUtc, string message)
    {
        lock (_sync)
        {
            _operations.TryGetValue(key, out var previous);
            _operations[key] = new(
                key,
                displayName,
                BackgroundOperationState.Failed,
                previous?.LastStartedAtUtc,
                previous?.LastCompletedAtUtc,
                nextRunAtUtc,
                previous?.AffectedCount,
                message);
            AddEvent(new(DateTime.UtcNow, displayName, BackgroundOperationState.Failed, message));
        }
    }

    private void AddEvent(BackgroundOperationEvent operationEvent)
    {
        _events.Insert(0, operationEvent);
        if (_events.Count > EventLimit)
        {
            _events.RemoveRange(EventLimit, _events.Count - EventLimit);
        }
    }
}
