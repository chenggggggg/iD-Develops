using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text;

namespace iD_Develops.Data
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Local";
            LoadDotEnvForEnvironment(environment);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("ApplicationDbContextConnection")
                ?? TryBuildConnectionStringFromEnvironment(configuration, environment);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Missing ConnectionStrings:ApplicationDbContextConnection for design-time EF. " +
                    "Set it via environment variable ConnectionStrings__ApplicationDbContextConnection or appsettings."
                );
            }

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new ApplicationDbContext(optionsBuilder.Options);
        }

        private static void LoadDotEnvForEnvironment(string environment)
        {
            var contentRoot = Directory.GetCurrentDirectory();
            var files = environment.ToLowerInvariant() switch
            {
                "local" => new[] { ".env" },
                "development" => new[] { ".env", ".env.development" },
                "staging" => new[] { ".env.staging" },
                "production" => new[] { ".env.production" },
                _ => Array.Empty<string>()
            };

            foreach (var file in files)
            {
                var path = Path.Combine(contentRoot, file);
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex].Trim();
                    var value = line[(separatorIndex + 1)..].Trim().Trim('"');

                    if (string.IsNullOrWhiteSpace(key) || Environment.GetEnvironmentVariable(key) != null)
                    {
                        continue;
                    }

                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }

        private static string? TryBuildConnectionStringFromEnvironment(IConfiguration configuration, string environment)
        {
            var normalizedEnvironment = environment.ToLowerInvariant();

            if (normalizedEnvironment == "local")
            {
                var databaseName = configuration["LOCAL_DB_NAME"] ?? "iddevelops_local";
                var password = configuration["LOCAL_POSTGRES_PASSWORD"];
                var user = configuration["LOCAL_POSTGRES_USER"] ?? "postgres";

                if (!string.IsNullOrWhiteSpace(password))
                {
                    return $"Host=localhost;Port=5432;Database={databaseName};Username={user};Password={password};";
                }
            }

            if (normalizedEnvironment == "development")
            {
                var databaseName = configuration["DEVELOPMENT_DB_NAME"] ?? "iddevelops_development";
                var password = configuration["LOCAL_POSTGRES_PASSWORD"];
                var user = configuration["LOCAL_POSTGRES_USER"] ?? "postgres";

                if (!string.IsNullOrWhiteSpace(password))
                {
                    return $"Host=localhost;Port=5433;Database={databaseName};Username={user};Password={password};";
                }
            }

            if (normalizedEnvironment == "staging")
            {
                var databaseName = configuration["DB_NAME"] ?? "iddevelops_staging";
                var password = configuration["POSTGRES_PASSWORD"];
                var user = configuration["POSTGRES_USER"] ?? "postgres";

                if (!string.IsNullOrWhiteSpace(password))
                {
                    return $"Host=localhost;Port=5434;Database={databaseName};Username={user};Password={password};";
                }
            }

            var fallbackDatabaseName = configuration["DB_NAME"];
            var fallbackPassword = configuration["POSTGRES_PASSWORD"];
            var fallbackUser = configuration["POSTGRES_USER"] ?? "postgres";
            if (!string.IsNullOrWhiteSpace(fallbackDatabaseName) && !string.IsNullOrWhiteSpace(fallbackPassword))
            {
                return $"Host=localhost;Port=5432;Database={fallbackDatabaseName};Username={fallbackUser};Password={fallbackPassword};";
            }

            return null;
        }
    }
}
