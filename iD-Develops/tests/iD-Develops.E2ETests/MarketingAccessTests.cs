using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

[Trait("SiteMode", "Marketing")]
public sealed class MarketingAccessTests : DiagnosticPageTest
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SiteModeTestSession.AssertModeAsync(Page, "Marketing");
    }

    [Theory]
    [InlineData("en-us", 1440)]
    [InlineData("nl-nl", 1440)]
    [InlineData("en-us", 390)]
    [InlineData("nl-nl", 390)]
    public async Task PublicInformationContactAndLevelTestStayAvailableWithoutAccountLinks(string culture, int width)
    {
        await Page.SetViewportSizeAsync(width, 900);
        foreach (var path in new[] { "", "/about", "/contact", "/level-test" })
        {
            var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}{path}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.Equal(200, response!.Status);
            await Expect(Page.Locator(".public-nav-login")).ToHaveCountAsync(0);
        }
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"marketing-level-test-{culture}-{width}.png"), FullPage = true });
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    public async Task CustomerAndTeacherCredentialsCannotCreateLoginSessions(string role)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await SiteModeTestSession.SetRoleAsync(account.User.Id, role);
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Staff sign in", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true })).ToHaveCountAsync(0);
        await Page.GetByLabel("E-mail").FillAsync(account.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(account.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Invalid login attempt.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.DoesNotContain(await Context.CookiesAsync(), cookie => cookie.Name == ".iDDevelops.Portal");
        Assert.Equal(403, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/profile"))!.Status);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task AdministratorsRetainDirectPortalAndProductManagementAccess(string role)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await SiteModeTestSession.SetRoleAsync(account.User.Id, role);
        await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password, "/profile", Page.Locator("[data-portal-page-content]"));
        Assert.Equal(200, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/products"))!.Status);
        await Expect(Page.GetByText(account.Standard.Name, new() { Exact = true }).First).ToBeVisibleAsync();
        Assert.Equal(200, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/products/{account.Standard.Slug}/edit"))!.Status);
        await Expect(Page.Locator("#product-edit-form")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RegistrationIsBlockedBeforeItsHandlerForAnonymousAndStaffVisitors()
    {
        Assert.Equal(404, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/register"))!.Status);
        var email = $"marketing-register-{Guid.NewGuid():N}@example.test";
        var status = await Page.EvaluateAsync<int>("""
            async args => (await fetch(args.url, { method: 'POST', headers: {'X-Requested-With':'XMLHttpRequest'},
                body: new URLSearchParams({'Input.Email':args.email,'Input.Password':'Valid!Password123','Input.ConfirmPassword':'Valid!Password123'}) })).status
            """, new { url = LocalTestApplication.PortalBaseUrl + "/register", email });
        Assert.Equal(404, status);
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.Users.AnyAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password, "/profile", Page.Locator("[data-portal-page-content]"));
        Assert.Equal(404, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/register"))!.Status);
        Assert.Equal(404, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/identity/account/externallogin"))!.Status);
    }

    [Fact]
    public async Task ExistingCustomerSessionIsRejectedAndCleared()
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await SiteModeTestSession.AddCustomerCookieAsync(Context, account.User);
        Assert.Contains(await Context.CookiesAsync(), cookie => cookie.Name == ".iDDevelops.Portal");
        var response = await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/profile");
        Assert.Equal(403, response!.Status);
        var remaining = (await Context.CookiesAsync()).Where(cookie => cookie.Name == ".iDDevelops.Portal")
            .Select(cookie => new { cookie.Name, cookie.Domain, cookie.Path, cookie.Expires }).ToArray();
        var deletionHeaders = (await response.HeadersArrayAsync()).Where(header => header.Name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
            .Select(header => string.Join(';', header.Value.Split(';').Skip(1))).ToArray();
        Assert.True(remaining.Length == 0, System.Text.Json.JsonSerializer.Serialize(new { remaining, deletionHeaders }));
    }

    [Theory]
    [InlineData("Standard")]
    [InlineData("Credit")]
    [InlineData("Signup")]
    public async Task ProductsShowInformationAndContactInsteadOfSignupCheckoutOrBooking(string type)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        var product = type == "Credit" ? account.Credit : account.Standard;
        await using var db = LocalTestApplication.CreateDbContext();
        if (type == "Signup")
            await db.CatalogProducts.Where(item => item.Id == product.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.ProductType, CatalogProductType.Signup));
        var errors = new List<string>();
        Page.PageError += (_, error) => errors.Add(error);
        Assert.Equal(200, (await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}", new() { WaitUntil = WaitUntilState.DOMContentLoaded }))!.Status);
        await Expect(Page.Locator("[data-marketing-product]")).ToBeVisibleAsync();
        await Expect(Page.Locator("#product-submit-form")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-signup-step]")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Contact us", Exact = true })).ToHaveAttributeAsync("href", "/en-us/contact");
        Assert.Empty(errors);
        var status = await Page.EvaluateAsync<int>("""
            async () => (await fetch(location.href, {method:'POST',headers:{'X-Requested-With':'XMLHttpRequest'},body:new URLSearchParams({email:'blocked@example.test'})})).status
            """);
        Assert.Equal(403, status);
        Assert.False(await db.CatalogPurchases.AnyAsync(purchase => purchase.CatalogProductId == product.Id));
    }

    [Fact]
    public async Task CustomerRecoveryAndConfirmationLinksCannotSendMailOrSignIn()
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/account/forgot-password", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.Locator("input[type='email']").FillAsync(account.User.Email!);
        await Page.Locator("form[method='post'] button[type='submit']").ClickAsync();
        await Expect(Page).ToHaveURLAsync($"{LocalTestApplication.PortalBaseUrl}/account/forgot-password-confirmation");
        var mailDirectory = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY")!;
        Assert.Empty(Directory.Exists(mailDirectory) ? Directory.GetFiles(mailDirectory, $"*{account.User.Email}*.html") : []);
        Assert.Equal(404, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/account/confirm-email?userId={account.User.Id}&code=YQ==&returnUrl=%2Fprofile"))!.Status);
        Assert.Equal(404, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/account/confirm-email-change?userId={account.User.Id}&email=changed%40example.test&code=YQ=="))!.Status);
        Assert.DoesNotContain(await Context.CookiesAsync(), cookie => cookie.Name == ".iDDevelops.Portal");
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.Equal(account.User.Email, (await db.Users.AsNoTracking().SingleAsync(user => user.Id == account.User.Id)).Email);
    }

    [Fact]
    public async Task AnonymousCompletionEmailsProtectedResultsWithoutCreatingAnAccount()
    {
        await using var data = await ExamAttemptFixture.CreateAsync();
        var email = $"marketing-result-{Guid.NewGuid():N}@example.test";
        var mailDirectory = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY")!;
        try
        {
            await Page.GotoAsync(data.AttemptUrl("en-us"), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(Page).ToHaveURLAsync(data.CompletionUrl("en-us"));
            await Page.GetByLabel("Name:", new() { Exact = true }).FillAsync("Marketing learner");
            await Page.GetByLabel("Email:", new() { Exact = true }).FillAsync(email);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Send my results!", Exact = true }).ClickAsync();
            await Expect(Page.Locator("[data-exam-completion] [role='status']")).ToBeVisibleAsync();
            var mail = File.ReadAllText(Assert.Single(Directory.GetFiles(mailDirectory, $"*{email}*.html")));
            var resultLink = Regex.Match(mail, "href=\"(?<url>[^\"]*/examination/results/[^\"]+)\"").Groups["url"].Value;
            Assert.False(string.IsNullOrWhiteSpace(resultLink));
            resultLink = WebUtility.HtmlDecode(resultLink);
            Assert.StartsWith(LocalTestApplication.PublicBaseUrl + "/", resultLink);
            Assert.Equal(200, (await Page.GotoAsync(resultLink, new() { WaitUntil = WaitUntilState.DOMContentLoaded }))!.Status);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = data.Exam.Name, Exact = true })).ToBeVisibleAsync();
            await using var db = LocalTestApplication.CreateDbContext();
            Assert.Null((await db.Records.AsNoTracking().SingleAsync(record => record.Id == data.Record.Id)).UserId);
            Assert.False(await db.Users.AnyAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));
        }
        finally
        {
            await using var cleanup = LocalTestApplication.CreateDbContext();
            await cleanup.Prospects.Where(prospect => prospect.Email == email).ExecuteDeleteAsync();
            if (Directory.Exists(mailDirectory))
                foreach (var file in Directory.GetFiles(mailDirectory, $"*{email}*")) File.Delete(file);
        }
    }
}

public sealed class PublicExamAccessTests : DiagnosticPageTest
{
    [Fact]
    public async Task PortalExamCannotBeAccessedOrSubmittedThroughThePublicLevelTestRoute()
    {
        await using var data = await ExamAttemptFixture.CreateAsync(expired: false, savedAnswer: false);
        await using var db = LocalTestApplication.CreateDbContext();
        await db.ExamVersions.Where(version => version.Id == data.Record.ExamVersionId)
            .ExecuteUpdateAsync(update => update.SetProperty(version => version.PublicSlug, (string?)null));
        Assert.Equal(404, (await Page.GotoAsync(data.AttemptUrl("en-us")))!.Status);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/contact", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var token = await Page.Locator("input[name='__RequestVerificationToken']").First.InputValueAsync();
        var status = await Page.EvaluateAsync<int>("""
            async args => (await fetch(args.url + '?handler=SubmitAnswer', {method:'POST',headers:{'X-Requested-With':'XMLHttpRequest','RequestVerificationToken':args.token},
                body:new URLSearchParams({recordId:args.recordId,examId:args.examId,questionId:args.questionId,answer:'Amsterdam'})})).status
            """, new { url = data.AttemptUrl("en-us"), token, recordId = data.Record.Id.ToString(), examId = data.Exam.Id.ToString(), questionId = data.Exam.Questions.First().Id.ToString() });
        Assert.Equal(404, status);
        Assert.Equal(ExamStatus.InProgress, (await data.ReadRecordAsync()).ExamStatus);
        Assert.False(await db.ParticipantAnswers.AnyAsync(answer => answer.RecordId == data.Record.Id));
    }
}

public sealed class FullSiteAccessTests : DiagnosticPageTest
{
    [Fact]
    public async Task ExistingCustomerCookieRemainsValidInFullMode()
    {
        await SiteModeTestSession.AssertModeAsync(Page, "Full");
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await SiteModeTestSession.AddCustomerCookieAsync(Context, account.User);
        Assert.Equal(200, (await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/profile"))!.Status);
        await Expect(Page.Locator("[data-portal-page-content]")).ToBeVisibleAsync();
    }
}

internal static class SiteModeTestSession
{
    public static async Task AssertModeAsync(IPage page, string expected)
    {
        var response = await page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/health/version");
        Assert.NotNull(response);
        Assert.Equal(expected, (await response.JsonAsync())!.Value.GetProperty("siteMode").GetString());
    }

    public static async Task SetRoleAsync(string userId, string role)
    {
        await using var db = LocalTestApplication.CreateDbContext();
        var targetRole = await db.Roles.SingleAsync(item => item.NormalizedName == role.ToUpperInvariant());
        await db.UserRoles.Where(item => item.UserId == userId).ExecuteDeleteAsync();
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = targetRole.Id });
        await db.SaveChangesAsync();
    }

    public static async Task AddCustomerCookieAsync(IBrowserContext browser, ApplicationUser user)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(LocalTestApplication.DatabaseConnectionString));
        services.AddDataProtection().SetApplicationName("iD-Develops").PersistKeysToDbContext<ApplicationDbContext>();
        services.AddAuthentication().AddCookie(IdentityConstants.ApplicationScheme);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(ClaimTypes.Role, "Student"), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!)
        ], IdentityConstants.ApplicationScheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        }, IdentityConstants.ApplicationScheme);
        await browser.AddCookiesAsync([new Microsoft.Playwright.Cookie
        {
            Name = ".iDDevelops.Portal", Value = options.TicketDataFormat.Protect(ticket),
            Url = LocalTestApplication.PortalBaseUrl, HttpOnly = true, SameSite = SameSiteAttribute.Lax
        }]);
    }
}
