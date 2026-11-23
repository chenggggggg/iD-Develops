using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class ApplicationDatabaseInitializerTests
{
    private const string OwnerEmail = "owner@example.test";
    private const string OwnerPassword = "Owner-Password-123!";

    [Fact]
    public async Task InitializeAsync_CreatesOneSuperAdminOwnerAndIsIdempotent()
    {
        using var fixture = new Fixture();
        var initializer = fixture.Initializer(OwnerSettings());

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        var owner = Assert.Single(await fixture.Db.Users.ToListAsync());
        Assert.Equal(OwnerEmail, owner.Email);
        Assert.True(owner.EmailConfirmed);
        Assert.True(await fixture.Users.CheckPasswordAsync(owner, OwnerPassword));
        Assert.Equal(["SuperAdmin"], await fixture.Users.GetRolesAsync(owner));
        Assert.Equal(["Admin", "Student", "SuperAdmin", "Teacher"], await fixture.Db.Roles.OrderBy(role => role.Name).Select(role => role.Name).ToListAsync());
    }

    [Fact]
    public async Task InitializeAsync_AddsOwnerRoleToExistingAdminWithoutResettingCredentialsOrProfile()
    {
        using var fixture = new Fixture();
        var user = await fixture.CreateUserAsync(OwnerEmail, "Admin");
        var id = user.Id;
        var passwordHash = user.PasswordHash;
        var securityStamp = user.SecurityStamp;
        var settings = OwnerSettings();
        settings["SuperAdmin:Password"] = "Different-Bootstrap-Password-456!";
        var initializer = fixture.Initializer(settings);

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        var owner = Assert.Single(await fixture.Db.Users.ToListAsync());
        Assert.Equal(id, owner.Id);
        Assert.Equal(passwordHash, owner.PasswordHash);
        Assert.Equal(securityStamp, owner.SecurityStamp);
        Assert.Equal("Existing", owner.FirstName);
        Assert.Equal("Owner", owner.LastName);
        Assert.Equal("0612345678", owner.PhoneNumber);
        Assert.True(await fixture.Users.CheckPasswordAsync(owner, OwnerPassword));
        Assert.False(await fixture.Users.CheckPasswordAsync(owner, settings["SuperAdmin:Password"]!));
        Assert.True(await fixture.Users.IsInRoleAsync(owner, "Admin"));
        Assert.True(await fixture.Users.IsInRoleAsync(owner, "SuperAdmin"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_LegacyAdminSettingsDoNotCreateOrPromoteAccounts(bool existingUser)
    {
        using var fixture = new Fixture();
        if (existingUser) await fixture.CreateUserAsync(OwnerEmail, "Student");
        var initializer = fixture.Initializer(new()
        {
            ["Admin:Email"] = OwnerEmail,
            ["Admin:Password"] = OwnerPassword
        });

        await initializer.InitializeAsync(CancellationToken.None);

        Assert.Equal(existingUser ? 1 : 0, await fixture.Db.Users.CountAsync());
        Assert.Empty(await fixture.Users.GetUsersInRoleAsync("SuperAdmin"));
        Assert.Empty(await fixture.Users.GetUsersInRoleAsync("Admin"));
        if (existingUser) Assert.True(await fixture.Users.IsInRoleAsync((await fixture.Users.FindByEmailAsync(OwnerEmail))!, "Student"));
    }

    [Fact]
    public async Task InitializeAsync_WithoutOwnerSettingsDoesNotPromoteFirstUser()
    {
        using var fixture = new Fixture();
        var student = await fixture.CreateUserAsync("first-registered@example.test", "Student");

        await fixture.Initializer(new()).InitializeAsync(CancellationToken.None);

        Assert.Single(await fixture.Db.Users.ToListAsync());
        Assert.Equal(["Student"], await fixture.Users.GetRolesAsync(student));
    }

    [Theory]
    [InlineData("SuperAdmin:Email", OwnerEmail)]
    [InlineData("SuperAdmin:Password", OwnerPassword)]
    public async Task InitializeAsync_IncompleteOwnerSettingsDoNotCreateAccount(string key, string value)
    {
        using var fixture = new Fixture();

        await fixture.Initializer(new() { [key] = value }).InitializeAsync(CancellationToken.None);

        Assert.Empty(await fixture.Db.Users.ToListAsync());
    }

    [Fact]
    public async Task InitializeAsync_OwnerSettingsDoNotCreateAdditionalLegacyAdmin()
    {
        using var fixture = new Fixture();
        var settings = OwnerSettings();
        settings["Admin:Email"] = "legacy-admin@example.test";
        settings["Admin:Password"] = OwnerPassword;

        await fixture.Initializer(settings).InitializeAsync(CancellationToken.None);

        Assert.Equal(OwnerEmail, Assert.Single(await fixture.Db.Users.ToListAsync()).Email);
        Assert.Empty(await fixture.Users.GetUsersInRoleAsync("Admin"));
    }

    private static Dictionary<string, string?> OwnerSettings() => new()
    {
        ["SuperAdmin:Email"] = OwnerEmail,
        ["SuperAdmin:Password"] = OwnerPassword
    };

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteTestDbFactory _factory = new();
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        private readonly RoleManager<IdentityRole> _roles;

        public Fixture()
        {
            Db = _factory.CreateDbContext(options => options.ReplaceService<IMigrator, ExistingSchemaMigrator>());
            Users = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(Db),
                Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
                [new UserValidator<ApplicationUser>()], [new PasswordValidator<ApplicationUser>()],
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), _services,
                NullLogger<UserManager<ApplicationUser>>.Instance);
            _roles = new RoleManager<IdentityRole>(new RoleStore<IdentityRole, ApplicationDbContext, string>(Db),
                [new RoleValidator<IdentityRole>()], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
                NullLogger<RoleManager<IdentityRole>>.Instance);
        }

        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> Users { get; }

        public ApplicationDatabaseInitializer Initializer(Dictionary<string, string?> settings) => new(
            Db, Users, _roles, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<ApplicationDatabaseInitializer>.Instance);

        public async Task<ApplicationUser> CreateUserAsync(string email, string role)
        {
            if (!await _roles.RoleExistsAsync(role)) Assert.True((await _roles.CreateAsync(new IdentityRole(role))).Succeeded);
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = "Existing",
                LastName = "Owner",
                PhoneNumber = "0612345678"
            };
            Assert.True((await Users.CreateAsync(user, OwnerPassword)).Succeeded);
            Assert.True((await Users.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }

        public void Dispose()
        {
            Users.Dispose();
            _roles.Dispose();
            _services.Dispose();
            Db.Dispose();
            _factory.Dispose();
        }
    }

    // SQLite's schema is already created by the fixture. Exercise real Identity stores
    // and startup seeding without running the unrelated PostgreSQL migration scripts.
    private sealed class ExistingSchemaMigrator : IMigrator
    {
        public ExistingSchemaMigrator() { }
        public void Migrate(string? targetMigration = null) { }
        public Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public string GenerateScript(string? fromMigration = null, string? toMigration = null, MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default) => throw new NotSupportedException();
        public bool HasPendingModelChanges() => false;
    }
}
