using System.Data;
using iD_Develops.Data;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Utilities;

// Session locks serialize checkout creation across app instances, including Stripe calls.
// Fulfillment uses the transaction variant so a failed transaction releases its lock.
public sealed class CheckoutDatabaseLock(ApplicationDbContext db, string key) : IAsyncDisposable
{
    private bool _opened;
    private bool _acquired;

    public async Task AcquireAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        _opened = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (_opened) await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({key}, 0))", ct);
        _acquired = true;
    }

    public async Task<bool> TryAcquireAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return true;
        _opened = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (_opened) await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@key, 0))";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        _acquired = await command.ExecuteScalarAsync(ct) is true;
        return _acquired;
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
            if (_acquired && db.Database.GetDbConnection().State == ConnectionState.Open)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({key}, 0))");
        }
        finally
        {
            if (_opened) await db.Database.CloseConnectionAsync();
        }
    }
}
