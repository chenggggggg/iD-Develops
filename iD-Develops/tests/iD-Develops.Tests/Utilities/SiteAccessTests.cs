using System.Security.Claims;
using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Utilities;

public sealed class SiteAccessTests
{
    public static TheoryData<bool, string?, string, string, string?, string?, int> Requests => new()
    {
        { false, null, "/Index", "GET", null, null, 200 },
        { false, null, "/Contact", "POST", null, null, 200 },
        { false, null, "/Portal/Examination/Level-Test-Introduction", "POST", null, null, 200 },
        { false, null, "/Portal/Examination/Level-Test", "POST", "SubmitAnswer", null, 200 },
        { false, null, "/Portal/Examination/Level-Test", "POST", "SubmitExam", null, 200 },
        { false, null, "/Examination/Completed", "POST", "SubmitForm", null, 200 },
        { false, null, "/Examination/Completed", "GET", "ClaimLevelTestResult", null, 404 },
        { false, null, "/Examination/Results", "GET", null, null, 200 },
        { false, null, "/Product", "GET", null, null, 200 },
        { false, null, "/Product", "POST", null, null, 403 },
        { false, null, "/Product", "POST", null, "Admin", 403 },
        { false, null, "/Product", "POST", "SaveContent", "Admin", 200 },
        { false, null, "/Product", "POST", "SaveContent", "Student", 403 },
        { false, null, "/FreeDownloads", "POST", "Create", null, 403 },
        { false, null, "/BounceWebhook", "POST", null, null, 200 },
        { false, null, "/PaymentResult", "GET", null, null, 200 },
        { true, "Identity", "/Account/Login", "POST", null, null, 200 },
        { true, "Identity", "/Account/LoginWith2fa", "POST", null, null, 200 },
        { true, "Identity", "/Account/LoginWithRecoveryCode", "POST", null, null, 200 },
        { true, "Identity", "/Account/Register", "GET", null, null, 404 },
        { true, "Identity", "/Account/Register", "POST", null, "Admin", 404 },
        { true, "Identity", "/Account/RegisterConfirmation", "GET", null, null, 404 },
        { true, "Identity", "/Account/ExternalLogin", "POST", null, null, 404 },
        { true, "Identity", "/Account/Manage/Index", "GET", null, null, 404 },
        { true, "Identity", "/Account/Manage/Index", "GET", null, "Admin", 200 },
        { true, null, "/Portal/Home/Index", "GET", null, null, 403 },
        { true, null, "/Portal/Home/Index", "GET", null, "Student", 403 },
        { true, null, "/Portal/Home/Index", "GET", null, "Teacher", 403 },
        { true, null, "/Portal/Home/Index", "GET", null, "Admin", 200 },
        { true, null, "/Portal/Admin/Products/Edit", "POST", "SaveContent", "SuperAdmin", 200 },
        { true, null, "/Portal/Schedule/Book", "POST", "Book", "Student", 403 },
        { true, null, "/Portal/Error", "GET", null, null, 200 }
    };

    public static IEnumerable<object?[]> FullRequests => Requests.Select(values => values.Take(6).ToArray());

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task MarketingEnforcesPublicAndStaffBoundaries(bool portal, string? area, string page,
        string method, string? handler, string? role, int expected)
        => Assert.Equal(expected, await ExecuteAsync(SiteMode.Marketing, portal, area, page, method, handler, role));

    [Theory]
    [MemberData(nameof(FullRequests))]
    public async Task FullLeavesExistingAuthorizationAndBusinessHandlersInControl(bool portal, string? area, string page,
        string method, string? handler, string? role)
        => Assert.Equal(200, await ExecuteAsync(SiteMode.Full, portal, area, page, method, handler, role));

    [Theory]
    [InlineData("/api/stripe-webhook")]
    [InlineData("/api/zoom/webhook")]
    public async Task SystemCallbacksContinueToTheirExistingSignatureValidation(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("portal.id.localhost");
        context.Request.Path = path;
        context.Request.Method = "POST";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new MarketingSystemEndpoint()), "signed callback"));
        var called = false;
        var middleware = new SiteAccessMiddleware(_ => { called = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context, Policy(SiteMode.Marketing), Urls());
        Assert.True(called);
    }

    [Theory]
    [InlineData("Full", false)]
    [InlineData("Marketing", true)]
    [InlineData(null, true)]
    public void ConfigurationSelectsModeWithSafeDefault(string? value, bool marketing)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            value == null ? [] : new Dictionary<string, string?> { ["SiteMode"] = value }).Build();
        using var services = new ServiceCollection().AddSiteAccess(configuration).BuildServiceProvider();
        var policy = services.GetRequiredService<ISiteAccessPolicy>();
        Assert.Equal(marketing, policy.IsMarketing);
        Assert.Equal(!marketing, policy.PublicAccountsEnabled);
        Assert.Equal(!marketing, policy.PublicTransactionsEnabled);
    }

    [Fact]
    public void UnknownNumericModeIsRejectedInsteadOfEnablingFullAccess()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SiteMode"] = "999" }).Build();
        using var services = new ServiceCollection().AddSiteAccess(configuration).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<SiteAccessOptions>>().Value);
    }

    [Theory]
    [InlineData("Admin", true, true)]
    [InlineData("SuperAdmin", true, true)]
    [InlineData("Teacher", true, false)]
    [InlineData("Student", true, false)]
    [InlineData("Admin", false, false)]
    public async Task MarketingIdentitySignInPreservesStaffAndEmailConfirmationChecks(string role, bool confirmed, bool allowed)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var user = new ApplicationUser { UserName = "staff@example.test", Email = "staff@example.test", EmailConfirmed = confirmed };
        var identityRole = new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() };
        db.Users.Add(user);
        db.Roles.Add(identityRole);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = identityRole.Id });
        await db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ISiteAccessPolicy>(Policy(SiteMode.Marketing));
        services.AddAuthentication();
        services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager<SiteSignInManager>();
        using var provider = services.BuildServiceProvider();
        var signIn = provider.GetRequiredService<SignInManager<ApplicationUser>>();
        Assert.IsType<SiteSignInManager>(signIn);
        Assert.Equal(allowed, await signIn.CanSignInAsync(user));
    }

    private static async Task<int> ExecuteAsync(SiteMode mode, bool portal, string? area, string page,
        string method, string? handler, string? role)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(portal ? "portal.id.localhost" : "id.localhost");
        context.Request.Method = method;
        if (handler != null) context.Request.QueryString = QueryString.Create("handler", handler);
        if (role != null) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new PageActionDescriptor { AreaName = area, ViewEnginePath = page }), page));
        var middleware = new SiteAccessMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, Policy(mode), Urls());
        return context.Response.StatusCode;
    }

    private static SiteAccessPolicy Policy(SiteMode mode)
        => new(Options.Create(new SiteAccessOptions { SiteMode = mode }));

    private static ApplicationUrlService Urls() => new(Options.Create(new ApplicationUrlOptions
    {
        PublicBaseUrl = "http://id.localhost:5000",
        PortalBaseUrl = "http://portal.id.localhost:5000"
    }));
}
