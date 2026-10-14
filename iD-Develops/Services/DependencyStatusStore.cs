namespace iD_Develops.Services;

public enum DependencyAvailability
{
    Starting,
    Healthy,
    Unavailable,
    Disabled
}

public sealed record DependencyStatusSnapshot(
    string Name,
    DependencyAvailability Availability,
    DateTimeOffset LastCheckedAt,
    string? ErrorCode = null)
{
    public bool IsHealthy => Availability == DependencyAvailability.Healthy;
}

public interface IDependencyStatusStore
{
    DependencyStatusSnapshot Database { get; }

    DependencyStatusSnapshot Stripe { get; }

    bool ReportDatabaseHealthy();

    bool ReportDatabaseUnavailable(string errorCode);

    bool ReportStripeHealthy();

    bool ReportStripeUnavailable(string errorCode);

    bool ReportStripeDisabled();

    Task WaitForDatabaseAsync(CancellationToken cancellationToken);
}

public sealed class DependencyStatusStore : IDependencyStatusStore
{
    private readonly object _sync = new();
    private DependencyStatusSnapshot _database = Starting("database");
    private DependencyStatusSnapshot _stripe = Starting("stripe");
    private TaskCompletionSource _databaseReady = NewSignal();

    public DependencyStatusSnapshot Database
    {
        get
        {
            lock (_sync)
            {
                return _database;
            }
        }
    }

    public DependencyStatusSnapshot Stripe
    {
        get
        {
            lock (_sync)
            {
                return _stripe;
            }
        }
    }

    public bool ReportDatabaseHealthy()
    {
        lock (_sync)
        {
            var changed = _database.Availability != DependencyAvailability.Healthy;
            _database = Healthy("database");
            _databaseReady.TrySetResult();
            return changed;
        }
    }

    public bool ReportDatabaseUnavailable(string errorCode)
    {
        lock (_sync)
        {
            var changed = _database.Availability != DependencyAvailability.Unavailable
                || !string.Equals(_database.ErrorCode, errorCode, StringComparison.Ordinal);

            if (_database.Availability == DependencyAvailability.Healthy || _databaseReady.Task.IsCompleted)
            {
                _databaseReady = NewSignal();
            }

            _database = Unavailable("database", errorCode);
            return changed;
        }
    }

    public bool ReportStripeHealthy()
        => UpdateStripe(Healthy("stripe"));

    public bool ReportStripeUnavailable(string errorCode)
        => UpdateStripe(Unavailable("stripe", errorCode));

    public bool ReportStripeDisabled()
        => UpdateStripe(new DependencyStatusSnapshot(
            "stripe",
            DependencyAvailability.Disabled,
            DateTimeOffset.UtcNow));

    public Task WaitForDatabaseAsync(CancellationToken cancellationToken)
    {
        Task readinessTask;
        lock (_sync)
        {
            readinessTask = _databaseReady.Task;
        }

        return readinessTask.WaitAsync(cancellationToken);
    }

    private bool UpdateStripe(DependencyStatusSnapshot next)
    {
        lock (_sync)
        {
            var changed = _stripe.Availability != next.Availability
                || !string.Equals(_stripe.ErrorCode, next.ErrorCode, StringComparison.Ordinal);
            _stripe = next;
            return changed;
        }
    }

    private static DependencyStatusSnapshot Starting(string name)
        => new(name, DependencyAvailability.Starting, DateTimeOffset.UtcNow);

    private static DependencyStatusSnapshot Healthy(string name)
        => new(name, DependencyAvailability.Healthy, DateTimeOffset.UtcNow);

    private static DependencyStatusSnapshot Unavailable(string name, string errorCode)
        => new(name, DependencyAvailability.Unavailable, DateTimeOffset.UtcNow, errorCode);

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
