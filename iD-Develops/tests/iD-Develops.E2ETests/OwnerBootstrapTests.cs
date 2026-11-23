using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class OwnerBootstrapTests : DiagnosticPageTest
{
    [Fact]
    public async Task ConfiguredOwnerCanViewAuditLogAndAssignAdminToAnotherAccount()
    {
        var email = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_EMAIL")
            ?? throw new InvalidOperationException("Configure the local startup owner's test email.");
        var password = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PASSWORD")
            ?? throw new InvalidOperationException("Configure the local startup owner's test password.");
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var ownerRoles = await (from user in db.Users
                                join assignment in db.UserRoles on user.Id equals assignment.UserId
                                join role in db.Roles on assignment.RoleId equals role.Id
                                where user.NormalizedEmail == email.ToUpperInvariant()
                                select role.Name).ToListAsync();
        Assert.Contains("SuperAdmin", ownerRoles);
        await BrowserLogin.SignInAsync(Page, email, password, "/admin/audit-log", Page.Locator("[data-audit-log]"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Audit log", Exact = true })).ToBeVisibleAsync();

        await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + "/admin/users", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = account.User.Email! });
        await Expect(row.Locator("[data-role-display]").GetByText("Student", new() { Exact = true })).ToBeVisibleAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "Edit role" }).ClickAsync();
        await row.GetByRole(AriaRole.Combobox).SelectOptionAsync("Admin");
        var response = await Page.RunAndWaitForResponseAsync(
            () => row.GetByRole(AriaRole.Button, new() { Name = "Confirm role change", Exact = true }).ClickAsync(),
            result => result.Request.Method == "POST" && new Uri(result.Url).AbsolutePath == "/admin/users");
        Assert.InRange(response.Status, 200, 399);
        await Expect(row.Locator("[data-role-display]").GetByText("Admin", new() { Exact = true })).ToBeVisibleAsync();
        var roles = await (from assignment in db.UserRoles
                           join role in db.Roles on assignment.RoleId equals role.Id
                           where assignment.UserId == account.User.Id
                           select role.Name).ToListAsync();
        Assert.Equal(["Admin"], roles);
    }
}
