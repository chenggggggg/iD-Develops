using iD_Develops.Data;

namespace iD_Develops.Utilities;

// Lock people in one order across booking, scheduling and cancellation paths.
public sealed class ScheduleDatabaseLock(ApplicationDbContext db) : IAsyncDisposable
{
    private readonly List<CheckoutDatabaseLock> _locks = [];

    public async Task AcquireAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        foreach (var id in userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Order(StringComparer.Ordinal))
        {
            var item = new CheckoutDatabaseLock(db, "schedule-person:" + id);
            _locks.Add(item);
            await item.AcquireAsync(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        for (var index = _locks.Count - 1; index >= 0; index--)
            await _locks[index].DisposeAsync();
    }
}
