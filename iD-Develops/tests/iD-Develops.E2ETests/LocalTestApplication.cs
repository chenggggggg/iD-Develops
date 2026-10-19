using iD_Develops.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace iD_Develops.E2ETests;

internal static class LocalTestApplication
{
    public static string PublicBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PUBLIC_BASE_URL") ?? "http://id.localhost:5000").TrimEnd('/');

    public static string PortalBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PORTAL_BASE_URL") ?? "http://portal.id.localhost:5000").TrimEnd('/');

    public static string DatabaseConnectionString
    {
        get
        {
            var connectionString = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_DATABASE_CONNECTION");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Set IDDEVELOPS_E2E_DATABASE_CONNECTION to the running local application's PostgreSQL connection string.");
            if (!IsLocalHost(new Uri(PublicBaseUrl).Host) || !IsLocalHost(new Uri(PortalBaseUrl).Host) ||
                !IsLocalHost(new NpgsqlConnectionStringBuilder(connectionString).Host ?? string.Empty))
                throw new InvalidOperationException("Database fixtures require local application and database hosts.");
            return connectionString;
        }
    }

    public static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(DatabaseConnectionString).Options);

    private static bool IsLocalHost(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
           host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
           (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
}
