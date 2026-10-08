using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class AdminUserServiceTests
{
    [Fact]
    public async Task GetUsersPageDataAsync_AdminCanOnlyManageNonPrivilegedUsers()
    {
        using var fixture = await TestFixture.CreateAsync();
        var actor = await fixture.CreateUserAsync("actor-admin@example.com", "Admin");
        var otherAdmin = await fixture.CreateUserAsync("other-admin@example.com", "Admin");
        var superAdmin = await fixture.CreateUserAsync("super-admin@example.com", "SuperAdmin");
        var teacher = await fixture.CreateUserAsync("teacher@example.com", "Teacher");
        var student = await fixture.CreateUserAsync("student@example.com", "Student");

        var result = await fixture.Service.GetUsersPageDataAsync(actor.Id);

        Assert.Equal(["Teacher", "Student"], result.AssignableRoles);
        Assert.False(FindUser(result, actor).CanManageRole);
        Assert.False(FindUser(result, otherAdmin).CanManageRole);
        Assert.False(FindUser(result, superAdmin).CanManageRole);
        Assert.True(FindUser(result, teacher).CanManageRole);
        Assert.True(FindUser(result, student).CanManageRole);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task SaveRoleAsync_AdminCannotChangePrivilegedUsers(string protectedRole)
    {
        using var fixture = await TestFixture.CreateAsync();
        var actor = await fixture.CreateUserAsync("actor-admin@example.com", "Admin");
        var protectedUser = await fixture.CreateUserAsync($"protected-{protectedRole.ToLowerInvariant()}@example.com", protectedRole);

        await fixture.Service.SaveRoleAsync(actor.Id, protectedUser.Id, "Teacher");

        Assert.True(await fixture.UserManager.IsInRoleAsync(protectedUser, protectedRole));
        Assert.False(await fixture.UserManager.IsInRoleAsync(protectedUser, "Teacher"));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task SaveRoleAsync_AdminCannotAssignPrivilegedRoles(string requestedRole)
    {
        using var fixture = await TestFixture.CreateAsync();
        var actor = await fixture.CreateUserAsync("actor-admin@example.com", "Admin");
        var student = await fixture.CreateUserAsync("student@example.com", "Student");

        await fixture.Service.SaveRoleAsync(actor.Id, student.Id, requestedRole);

        Assert.True(await fixture.UserManager.IsInRoleAsync(student, "Student"));
        Assert.False(await fixture.UserManager.IsInRoleAsync(student, requestedRole));
    }

    [Fact]
    public async Task SaveRoleAsync_AdminCanChangeStudentToTeacher()
    {
        using var fixture = await TestFixture.CreateAsync();
        var actor = await fixture.CreateUserAsync("actor-admin@example.com", "Admin");
        var student = await fixture.CreateUserAsync("student@example.com", "Student");

        await fixture.Service.SaveRoleAsync(actor.Id, student.Id, "Teacher");

        Assert.True(await fixture.UserManager.IsInRoleAsync(student, "Teacher"));
        Assert.False(await fixture.UserManager.IsInRoleAsync(student, "Student"));
    }

    [Fact]
    public async Task SaveRoleAsync_SuperAdminCanChangeAdminToTeacher()
    {
        using var fixture = await TestFixture.CreateAsync();
        var actor = await fixture.CreateUserAsync("actor-super-admin@example.com", "SuperAdmin");
        var admin = await fixture.CreateUserAsync("admin@example.com", "Admin");

        await fixture.Service.SaveRoleAsync(actor.Id, admin.Id, "Teacher");

        Assert.True(await fixture.UserManager.IsInRoleAsync(admin, "Teacher"));
        Assert.False(await fixture.UserManager.IsInRoleAsync(admin, "Admin"));
    }

    [Fact]
    public async Task SaveRoleAsync_NonAdministratorCannotChangeRoles()
    {
        using var fixture = await TestFixture.CreateAsync();
        var actor = await fixture.CreateUserAsync("teacher@example.com", "Teacher");
        var student = await fixture.CreateUserAsync("student@example.com", "Student");

        await fixture.Service.SaveRoleAsync(actor.Id, student.Id, "Teacher");

        Assert.True(await fixture.UserManager.IsInRoleAsync(student, "Student"));
        Assert.False(await fixture.UserManager.IsInRoleAsync(student, "Teacher"));
    }

    private static AdminUserListItem FindUser(AdminUsersPageData result, ApplicationUser user)
    {
        return Assert.Single(result.Users, item => item.Id == user.Id);
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly SqliteTestDbFactory _factory;
        private readonly ApplicationDbContext _dbContext;
        private readonly ServiceProvider _services;
        private readonly RoleManager<IdentityRole> _roleManager;

        private TestFixture(
            SqliteTestDbFactory factory,
            ApplicationDbContext dbContext,
            ServiceProvider services,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _factory = factory;
            _dbContext = dbContext;
            _services = services;
            UserManager = userManager;
            _roleManager = roleManager;
            Service = new AdminUserService(dbContext, userManager, roleManager);
        }

        public AdminUserService Service { get; }

        public UserManager<ApplicationUser> UserManager { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var factory = new SqliteTestDbFactory();
            var dbContext = factory.CreateDbContext();
            var services = new ServiceCollection().BuildServiceProvider();
            var userManager = CreateUserManager(dbContext, services);
            var roleManager = CreateRoleManager(dbContext);
            var fixture = new TestFixture(factory, dbContext, services, userManager, roleManager);

            foreach (var roleName in new[] { "SuperAdmin", "Admin", "Teacher", "Student" })
            {
                Assert.True((await roleManager.CreateAsync(new IdentityRole(roleName))).Succeeded);
            }

            return fixture;
        }

        public async Task<ApplicationUser> CreateUserAsync(string email, string roleName)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email
            };

            Assert.True((await UserManager.CreateAsync(user)).Succeeded);
            Assert.True((await UserManager.AddToRoleAsync(user, roleName)).Succeeded);
            return user;
        }

        public void Dispose()
        {
            UserManager.Dispose();
            _roleManager.Dispose();
            _services.Dispose();
            _dbContext.Dispose();
            _factory.Dispose();
        }

        private static UserManager<ApplicationUser> CreateUserManager(
            ApplicationDbContext dbContext,
            IServiceProvider services)
        {
            var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(dbContext);
            return new UserManager<ApplicationUser>(
                store,
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                [new UserValidator<ApplicationUser>()],
                [new PasswordValidator<ApplicationUser>()],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services,
                NullLogger<UserManager<ApplicationUser>>.Instance);
        }

        private static RoleManager<IdentityRole> CreateRoleManager(ApplicationDbContext dbContext)
        {
            var store = new RoleStore<IdentityRole, ApplicationDbContext, string>(dbContext);
            return new RoleManager<IdentityRole>(
                store,
                [new RoleValidator<IdentityRole>()],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                NullLogger<RoleManager<IdentityRole>>.Instance);
        }
    }
}
