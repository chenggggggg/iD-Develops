using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

namespace iD_Develops.E2ETests;

public sealed class AuditLogInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task DefaultTodayDateRangeAllDatesAndResetUseAmsterdamDates()
    {
        await using var data = await AuditFixture.CreateAsync();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, AuditLogModel.BusinessTimeZone).DateTime);
        await data.SeedAsync(today, 2);
        await data.SeedAsync(today.AddDays(-1), 1);
        await data.SeedAsync(today.AddDays(-5), 1);
        await LoginAsync(data, $"/admin/audit-log?q={data.Marker}");
        await Expect(Page.GetByLabel("From date", new() { Exact = true })).ToHaveValueAsync(today.ToString("yyyy-MM-dd"));
        await Expect(Page.GetByLabel("To date", new() { Exact = true })).ToHaveValueAsync(today.ToString("yyyy-MM-dd"));
        await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(2);
        await Expect(Page.Locator("[data-audit-count]")).ToHaveTextAsync("Showing 2 of 2 matching events.");
        await Expect(Page.Locator("[data-audit-event] time").First).ToContainTextAsync("12:00:00");
        await Page.GetByLabel("From date", new() { Exact = true }).FillAsync(today.AddDays(-1).ToString("yyyy-MM-dd"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(3);
        await Page.GetByLabel("All dates", new() { Exact = true }).CheckAsync();
        await Expect(Page.GetByLabel("From date", new() { Exact = true })).ToBeDisabledAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(4);
        await Page.SetViewportSizeAsync(1440, 1000);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "audit-log-desktop.png") });
        await Page.SetViewportSizeAsync(390, 844);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "audit-log-mobile.png") });
        Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await Page.GetByRole(AriaRole.Link, new() { Name = "Clear filters", Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("From date", new() { Exact = true })).ToHaveValueAsync(today.ToString("yyyy-MM-dd"));
        await Expect(Page.GetByLabel("All dates", new() { Exact = true })).Not.ToBeCheckedAsync();
        await Expect(Page.GetByLabel("Search", new() { Exact = true })).ToHaveValueAsync("");
    }

    [Fact]
    public async Task LoadMorePreservesFiltersAndExpandedRowsAndAllowsRetryWithoutDuplicateRequests()
    {
        await using var data = await AuditFixture.CreateAsync();
        var date = new DateOnly(2026, 3, 29);
        await data.SeedAsync(date, 61);
        await data.SeedAsync(date, 2, AuditEventOutcome.Success);
        await data.SeedAsync(date.AddDays(1), 2);
        await LoginAsync(data, $"/admin/audit-log?q={data.Marker}");
        await Page.GetByLabel("From date", new() { Exact = true }).FillAsync(date.ToString("yyyy-MM-dd"));
        await Page.GetByLabel("To date", new() { Exact = true }).FillAsync(date.ToString("yyyy-MM-dd"));
        await Expect(Page.GetByRole(AriaRole.Combobox, new() { Name = "Category", Exact = true })).ToBeVisibleAsync();
        await Page.GetByLabel("Category", new() { Exact = true }).SelectOptionAsync("Commerce");
        await Page.GetByLabel("Outcome", new() { Exact = true }).SelectOptionAsync("Failure");
        await Page.GetByLabel("Results per load", new() { Exact = true }).SelectOptionAsync("25");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(25);
        await Expect(Page.Locator("[data-audit-count]")).ToHaveTextAsync("Showing 25 of 61 matching events.");
        var firstRow = Page.Locator("[data-audit-event]").First;
        var firstId = await firstRow.GetAttributeAsync("data-audit-event");
        await firstRow.Locator("summary").ClickAsync();
        await Expect(firstRow.GetByRole(AriaRole.Region, new() { Name = "Payment and delivery details" })).ToBeVisibleAsync();
        await Expect(firstRow.GetByRole(AriaRole.Link, new() { Name = "View related checkout events" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("AllDates=True|allDates=true"));
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        await Page.RouteAsync("**/admin/audit-log?*", async route =>
        {
            if (!route.Request.Url.Contains("handler=More", StringComparison.Ordinal)) { await route.ContinueAsync(); return; }
            requests++;
            Assert.Contains(data.Marker, route.Request.Url);
            Assert.Contains("Category=Commerce", route.Request.Url);
            Assert.Contains("Outcome=Failure", route.Request.Url);
            Assert.Contains("PageSize=25", route.Request.Url);
            Assert.Contains("From=2026-03-29", route.Request.Url);
            Assert.Contains("To=2026-03-29", route.Request.Url);
            if (requests == 1)
            {
                requested.TrySetResult();
                await release.Task;
                await route.FulfillAsync(new() { Status = 503, Body = "Temporary failure" });
            }
            else await route.ContinueAsync();
        });
        var more = Page.GetByRole(AriaRole.Link, new() { Name = "Load more", Exact = true });
        await more.ClickAsync();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Expect(Page.Locator("[data-audit-more-spinner]")).ToBeVisibleAsync();
        await Page.EvaluateAsync("() => document.querySelector('[data-audit-load-more]').click()");
        Assert.Equal(1, requests);
        release.TrySetResult();
        await Expect(Page.Locator("[data-audit-status]")).ToHaveTextAsync("Could not load more events. Please try again.");
        await Expect(Page.Locator("[data-audit-more-spinner]")).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(25);
        await more.ClickAsync();
        await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(50);
        await Expect(Page.Locator("[data-audit-count]")).ToHaveTextAsync("Showing 50 of 61 matching events.");
        await Expect(Page.Locator($"[data-audit-event='{firstId}']")).ToHaveJSPropertyAsync("open", true);
        await more.ClickAsync();
        await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(61);
        await Expect(more).ToHaveCountAsync(0);
        await Expect(Page.GetByText("No more matching events.", new() { Exact = true })).ToBeVisibleAsync();
        var ids = await Page.Locator("[data-audit-event]").EvaluateAllAsync<string[]>("elements => elements.map(element => element.dataset.auditEvent)");
        Assert.Equal(61, ids.Distinct().Count());
    }

    [Fact]
    public async Task InvalidDateRangeShowsValidationAndDoesNotFetchAllDates()
    {
        await using var data = await AuditFixture.CreateAsync();
        await LoginAsync(data, "/admin/audit-log");
        await Page.GetByLabel("From date", new() { Exact = true }).FillAsync("2026-11-04");
        await Page.GetByLabel("To date", new() { Exact = true }).FillAsync("2026-11-03");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("start date on or before the end date");
        await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StudentsAndAdministratorsCannotReadAuditPagesOrLoadMore(bool administrator)
    {
        await using var data = await AuditFixture.CreateAsync(superAdmin: false, administrator: administrator);
        await LoginAsync(data, "/profile");
        foreach (var path in new[] { "/admin/audit-log", "/admin/audit-log?handler=More&AllDates=true" })
        {
            var response = await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + path);
            Assert.True(response?.Status == 403 || Page.Url.Contains("/access-denied", StringComparison.Ordinal));
            await Expect(Page.Locator("[data-audit-event]")).ToHaveCountAsync(0);
        }
    }

    private async Task LoginAsync(AuditFixture data, string path)
    {
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(path)}");
        await Page.GetByLabel("E-mail").FillAsync(data.Account.User.Email!);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(data.Account.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(LocalTestApplication.PortalBaseUrl + path);
    }

    private sealed class AuditFixture(CatalogBrowsingFixture account) : IAsyncDisposable
    {
        public CatalogBrowsingFixture Account { get; } = account;
        public string Marker { get; } = "audit-" + Guid.NewGuid().ToString("N");
        public static async Task<AuditFixture> CreateAsync(bool superAdmin = true, bool administrator = false)
        {
            var account = await CatalogBrowsingFixture.CreateAsync(administrator);
            try
            {
                if (superAdmin)
                {
                    await using var db = LocalTestApplication.CreateDbContext();
                    var role = await db.Roles.SingleAsync(item => item.NormalizedName == "SUPERADMIN");
                    db.UserRoles.Add(new IdentityUserRole<string> { UserId = account.User.Id, RoleId = role.Id });
                    await db.SaveChangesAsync();
                }
                return new(account);
            }
            catch { await account.DisposeAsync(); throw; }
        }

        public async Task SeedAsync(DateOnly date, int count, AuditEventOutcome outcome = AuditEventOutcome.Failure)
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(12, 0)), AuditLogModel.BusinessTimeZone);
            await using var db = LocalTestApplication.CreateDbContext();
            db.ApplicationAuditEvents.AddRange(Enumerable.Range(0, count).Select(index => new ApplicationAuditEvent
            {
                EventType = "E2E.AuditFilter",
                Category = AuditEventCategory.Commerce,
                Outcome = outcome,
                Summary = "Test payment delivery event " + index,
                OccurredAtUtc = utc,
                SubjectUserId = Account.User.Id,
                MetadataJson = JsonSerializer.Serialize(new { checkoutAttemptId = Marker, fulfillmentStatus = "Failed" })
            }));
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var db = LocalTestApplication.CreateDbContext();
                await db.ApplicationAuditEvents.Where(item => item.SubjectUserId == Account.User.Id || item.ActorUserId == Account.User.Id).ExecuteDeleteAsync();
            }
            finally { await Account.DisposeAsync(); }
        }
    }
}
