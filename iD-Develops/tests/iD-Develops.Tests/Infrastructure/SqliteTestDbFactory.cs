using iD_Develops.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Infrastructure;

internal sealed class SqliteTestDbFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDbFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public DbContextOptions<ApplicationDbContext> Options => new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;

    public ApplicationDbContext CreateDbContext(Action<DbContextOptionsBuilder<ApplicationDbContext>>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection);
        configure?.Invoke(builder);

        var dbContext = new ApplicationDbContext(builder.Options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
