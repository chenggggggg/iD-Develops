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

    public ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        var dbContext = new ApplicationDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
