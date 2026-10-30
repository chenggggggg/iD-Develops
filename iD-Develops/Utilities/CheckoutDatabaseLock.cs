using System.Data;
using iD_Develops.Data;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Utilities;

// Session locks serialize checkout creation across app instances, including Stripe calls.
// Fulfillment uses the transaction variant so a failed transaction releases its lock.
public sealed class CheckoutDatabaseLock(ApplicationDbContext db, string key) : IAsyncDisposable
{
    private bool _opened;

    public async Task AcquireAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        _opened = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (_opened) await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({key}, 0))", ct);
    }

    public static Task AcquireTransactionAsync(ApplicationDbContext context, string value, CancellationToken ct)
        => context.Database.IsNpgsql()
            ? context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({value}, 0))", ct)
            : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!db.Database.IsNpgsql()) return;
        try
        {
            if (db.Database.GetDbConnection().State == ConnectionState.Open)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({key}, 0))");
        }
        finally
        {
            if (_opened) await db.Database.CloseConnectionAsync();
        }
    }
}
