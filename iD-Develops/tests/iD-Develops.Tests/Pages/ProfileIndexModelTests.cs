using System.Security.Claims;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Profile;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Pages;

public sealed class ProfileIndexModelTests
{
    [Fact]
    public async Task OnGetAsync_LoadsAndOrdersActiveCreditBalances_WithRelationalProvider()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        using var services = new ServiceCollection().BuildServiceProvider();
        using var userManager = CreateUserManager(db, services);
        using var roleManager = CreateRoleManager(db);

        await roleManager.CreateAsync(new IdentityRole("Student"));
        var user = new ApplicationUser
        {
            UserName = "profile@example.com",
            Email = "profile@example.com",
            FirstName = "Profile",
            LastName = "Learner"
        };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, "Student")).Succeeded);

        var conversation = new CreditType
        {
            Name = "Conversation",
            NormalizedName = "CONVERSATION",
            SingularLabel = "session",
            PluralLabel = "sessions"
        };
        var assessment = new CreditType
        {
            Name = "Assessment",
            NormalizedName = "ASSESSMENT",
            SingularLabel = "attempt",
            PluralLabel = "attempts"
        };
        db.CreditTypes.AddRange(conversation, assessment);
        db.UserCreditLots.AddRange(
            new UserCreditLot
            {
                User = user,
                CreditType = conversation,
                GrantedQuantity = 3,
                RemainingQuantity = 2,
                GrantedAtUtc = DateTime.UtcNow
            },
            new UserCreditLot
            {
                User = user,
                CreditType = assessment,
                GrantedQuantity = 1,
                RemainingQuantity = 1,
                GrantedAtUtc = DateTime.UtcNow
            },
            new UserCreditLot
            {
                User = user,
                CreditType = conversation,
                GrantedQuantity = 4,
                RemainingQuantity = 4,
                GrantedAtUtc = DateTime.UtcNow.AddMonths(-2),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(-1)
            });
        await db.SaveChangesAsync();

        var model = new IndexModel(db, userManager)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user.Id)],
                        "Test"))
                }
            }
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal("Profile Learner", model.DisplayName);
        Assert.Equal("Student", model.Role);
        Assert.Collection(
            model.CreditBalances,
            balance =>
            {
                Assert.Equal("Assessment", balance.Name);
                Assert.Equal(1, balance.Quantity);
            },
            balance =>
            {
                Assert.Equal("Conversation", balance.Name);
                Assert.Equal(2, balance.Quantity);
            });
    }

    private static UserManager<ApplicationUser> CreateUserManager(
        ApplicationDbContext db,
        IServiceProvider services)
    {
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db);
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

    private static RoleManager<IdentityRole> CreateRoleManager(ApplicationDbContext db)
    {
        var store = new RoleStore<IdentityRole, ApplicationDbContext, string>(db);
        return new RoleManager<IdentityRole>(
            store,
            [new RoleValidator<IdentityRole>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);
    }
}
