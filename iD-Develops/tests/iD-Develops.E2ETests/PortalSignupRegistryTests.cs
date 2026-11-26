using System.Text.Json;
using iD_Develops.Data.Migrations;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class PortalSignupRegistryTests : DiagnosticPageTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaffCanFilterSortPageAndReturnFromDetailsWithoutLosingTheirPlace(bool mobile)
    {
        await using var fixture = await RegistryFixture.CreateAsync();
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 900);
        await BrowserLogin.SignInAsync(Page, fixture.Account.User.Email!, fixture.Account.Password,
            "/products/signups", Page.Locator("[data-signup-registry]"));
        var response = await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Contains("no-store", (await response!.AllHeadersAsync())["cache-control"]);
        await Page.GetByLabel("Product/form", new() { Exact = true }).SelectOptionAsync("product-" + fixture.Account.Standard.Id);
        await Page.GetByLabel("Search customers", new() { Exact = true }).FillAsync(fixture.Marker);
        await OpenAdvancedAsync();
        await Page.GetByLabel("Sort by", new() { Exact = true }).SelectOptionAsync("oldest");
        await ApplyAsync();
        var rows = Page.Locator("[data-signup-row]:visible");
        await Expect(rows).ToHaveCountAsync(25);
        await Expect(rows.First).ToContainTextAsync("Learner 00");
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await ScreenshotAsync("list-" + mobile);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true }).ClickAsync();
        await Expect(rows).ToHaveCountAsync(6);
        await Expect(rows.First).ToContainTextAsync("Learner 25");
        var listUrl = Page.Url;
        await rows.First.GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Page.Locator("[data-signup-details]")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Contact details", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-signup-answers]")).ToContainTextAsync("Which days would you prefer?");
        await Expect(Page.Locator("[data-signup-answers]")).ToContainTextAsync("Monday");
        await Expect(Page.Locator("[data-signup-answers]")).ToContainTextAsync("Wednesday");
        await Expect(Page.Locator("[data-signup-answers]")).Not.ToContainTextAsync("preferred_days");
        await Expect(Page.GetByText("Retry scheduled", new() { Exact = false })).ToBeVisibleAsync();
        await ScreenshotAsync("details-" + mobile);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to signups", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(listUrl);
        await Expect(rows.First).ToContainTextAsync("Learner 25");
        await OpenAdvancedAsync();
        await Page.GetByLabel("Sort by", new() { Exact = true }).SelectOptionAsync("name-desc");
        await ApplyAsync();
        await Expect(rows.First).ToContainTextAsync("Learner 30");
        await OpenAdvancedAsync();
        await Page.GetByLabel("From date", new() { Exact = true }).FillAsync("2026-01-10");
        await Page.GetByLabel("To date", new() { Exact = true }).FillAsync("2026-01-12");
        await ApplyAsync();
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.First).ToContainTextAsync("Learner 11");
        await Page.GetByLabel("Search customers", new() { Exact = true }).FillAsync("no-matching-person-" + fixture.Marker);
        await ApplyAsync();
        await Expect(Page.GetByText("No signups match these filters.", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task AuthorizedStaffCanOpenHistoricalDetailsAndExternalReturnUrlsAreRejected(string role)
    {
        await using var fixture = await RegistryFixture.CreateAsync(role);
        var path = $"/products/signups/{fixture.HistoricalId}?returnUrl=https%3A%2F%2Fevil.example.invalid%2F";
        await BrowserLogin.SignInAsync(Page, fixture.Account.User.Email!, fixture.Account.Password, path, Page.Locator("[data-signup-details]"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Back to signups", Exact = true })).ToHaveAttributeAsync("href", "/products/signups");
        await Expect(Page.GetByText("The saved answers could not be read.", new() { Exact = false })).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-signup-answers]")).ToContainTextAsync("legacy question");
        await Expect(Page.Locator("[data-signup-answers]")).ToContainTextAsync("<script>window.untrustedSignup=true</script>");
        Assert.Null(await Page.EvaluateAsync<object?>("window.untrustedSignup ?? null"));
        await Expect(Page.GetByText("Original question labels were not recorded.", new() { Exact = false })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to signups", Exact = true }).ClickAsync();
        var historicalKey = SignupRegistryService.GroupKey(null, fixture.HistoricalName, "old-form-" + fixture.Marker);
        await Page.GetByLabel("Product/form", new() { Exact = true }).SelectOptionAsync(historicalKey);
        await ApplyAsync();
        await Expect(Page.Locator("[data-signup-row]:visible")).ToHaveCountAsync(1);
        await Expect(Page.GetByText("This historical group uses the saved form name and slug.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-signup-row]:visible")).ToContainTextAsync("Not available");
    }

    [Fact]
    public async Task AnonymousVisitorsMustSignInAndThePublicHostDoesNotExposeSignupRecords()
    {
        await using var fixture = await RegistryFixture.CreateAsync();
        foreach (var path in new[] { "/products/signups", $"/products/signups/{fixture.HistoricalId}" })
        {
            await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.StartsWith(LocalTestApplication.PortalBaseUrl + "/login?", Page.Url);
            await Expect(Page.Locator("[data-signup-registry], [data-signup-details]")).ToHaveCountAsync(0);
            var response = await Page.GotoAsync(LocalTestApplication.PublicBaseUrl + path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.Equal(404, response!.Status);
            await Expect(Page.GetByText(fixture.Marker, new() { Exact = false })).ToHaveCountAsync(0);
        }
    }

    [Theory]
    [Trait("SiteMode", "Full")]
    [InlineData("Student")]
    [InlineData("Teacher")]
    public async Task OtherRolesCannotReadTheListOrGuessADetailId(string role)
    {
        await using var fixture = await RegistryFixture.CreateAsync(role);
        await BrowserLogin.SignInAsync(Page, fixture.Account.User.Email!, fixture.Account.Password,
            "/profile", Page.Locator("[data-portal-page-content]"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Signups", Exact = true })).ToHaveCountAsync(0);
        foreach (var path in new[] { "/products/signups", $"/products/signups/{fixture.HistoricalId}" })
        {
            await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.StartsWith(LocalTestApplication.PortalBaseUrl + "/access-denied", Page.Url);
            await Expect(Page.Locator("[data-signup-registry], [data-signup-details]")).ToHaveCountAsync(0);
        }
    }

    [Fact]
    public async Task HistoricalBackfillPreservesOriginalJsonAndSkipsMalformedPayloadsWithoutGuessingProductIds()
    {
        await using var db = LocalTestApplication.CreateDbContext();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var original = """{"signup_full_name":"Historical learner","product_slug":"old-course","question":"Saved answer"}""";
            var valid = new ProductFormSubmission { FormName = "Historical backfill", UserEmail = "legacy@example.test", SubmissionDataJson = original };
            var invalid = new ProductFormSubmission { FormName = "Unreadable backfill", UserEmail = "legacy@example.test", SubmissionDataJson = "{broken" };
            var unsupported = new ProductFormSubmission { FormName = "Unsupported JSON", UserEmail = "legacy@example.test", SubmissionDataJson = """{"question":"\u0000"}""" };
            db.ProductFormSubmissions.AddRange(valid, invalid, unsupported);
            await db.SaveChangesAsync();
            foreach (var operation in new AddSignupRegistry().UpOperations.OfType<SqlOperation>())
                await db.Database.ExecuteSqlRawAsync(operation.Sql);
            db.ChangeTracker.Clear();
            var saved = await db.ProductFormSubmissions.SingleAsync(item => item.Id == valid.Id);
            Assert.Equal("Historical learner", saved.CustomerName);
            Assert.Equal("old-course", saved.ProductSlug);
            Assert.Null(saved.CatalogProductId);
            Assert.Equal(original, saved.SubmissionDataJson);
            Assert.Equal("{broken", (await db.ProductFormSubmissions.SingleAsync(item => item.Id == invalid.Id)).SubmissionDataJson);
            Assert.Equal(unsupported.SubmissionDataJson, (await db.ProductFormSubmissions.SingleAsync(item => item.Id == unsupported.Id)).SubmissionDataJson);
            await transaction.RollbackAsync();
        });
    }

    private async Task OpenAdvancedAsync()
    {
        var options = Page.Locator("[data-signup-advanced]");
        if (await options.GetAttributeAsync("open") == null)
            await options.Locator("summary").ClickAsync();
    }

    private async Task ApplyAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-signup-registry]")).ToBeVisibleAsync();
    }

    private async Task ScreenshotAsync(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-results", "signup-registry");
        Directory.CreateDirectory(path);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(path, name + ".png") });
    }

    private sealed class RegistryFixture : IAsyncDisposable
    {
        public CatalogBrowsingFixture Account { get; private set; } = null!;
        public string Marker { get; } = "Registry-" + Guid.NewGuid().ToString("N");
        public string HistoricalName => "Historical " + Marker;
        public int HistoricalId { get; private set; }
        private readonly List<int> _ids = [];
        private readonly List<string> _mailKeys = [];
        public static async Task<RegistryFixture> CreateAsync(string role = "Admin")
        {
            var result = new RegistryFixture { Account = await CatalogBrowsingFixture.CreateAsync(administrator: role is "Admin" or "SuperAdmin") };
            try
            {
                await using var db = LocalTestApplication.CreateDbContext();
                if (role is "SuperAdmin" or "Teacher")
                {
                    await db.UserRoles.Where(item => item.UserId == result.Account.User.Id).ExecuteDeleteAsync();
                    db.UserRoles.Add(new IdentityUserRole<string> { UserId = result.Account.User.Id, RoleId = await db.Roles.Where(item => item.Name == role).Select(item => item.Id).SingleAsync() });
                }
                for (var index = 0; index < 31; index++)
                {
                    var row = new ProductFormSubmission
                    {
                        CatalogProductId = result.Account.Standard.Id,
                        FormName = result.Account.Standard.Name,
                        ProductSlug = result.Account.Standard.Slug,
                        CustomerName = result.Marker + $" Learner {index:00}",
                        UserEmail = $"{result.Marker}-{index:00}@example.test",
                        RequestKey = "registry-" + Guid.NewGuid().ToString("N"),
                        SubmissionDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(index),
                        SubmissionDataJson = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["signup_phone"] = "+31 20 123 4567",
                            ["preferred_days"] = "Monday\nWednesday",
                            ["_form_fields"] = new[] { new ProductSubmissionField("preferred_days", "Which days would you prefer?") }
                        })
                    };
                    db.ProductFormSubmissions.Add(row);
                    if (index is 0 or 1 or 25)
                    {
                        var mailKey = "signup:" + row.RequestKey;
                        result._mailKeys.Add(mailKey);
                        db.CatalogCheckoutMails.Add(new()
                        {
                            StripeSessionId = mailKey,
                            Recipient = row.UserEmail,
                            Subject = "Signup confirmation",
                            SentAtUtc = index == 1 ? null : DateTime.UtcNow,
                            NextAttemptAtUtc = DateTime.UtcNow.AddDays(1)
                        });
                        if (index == 25)
                            db.CatalogCheckoutMails.Add(new()
                            {
                                StripeSessionId = mailKey,
                                Recipient = "company@example.test",
                                Subject = "New signup",
                                Attempts = 1,
                                NextAttemptAtUtc = DateTime.UtcNow.AddDays(1)
                            });
                    }
                }
                var historical = new ProductFormSubmission
                {
                    FormName = result.HistoricalName,
                    ProductSlug = "old-form-" + result.Marker,
                    UserEmail = "historical@example.test",
                    CustomerName = "Historical learner",
                    SubmissionDataJson = """{"legacy_question":"<script>window.untrustedSignup=true</script>"}"""
                };
                db.ProductFormSubmissions.Add(historical);
                db.ProductFormSubmissions.Add(new ProductFormSubmission
                {
                    CatalogProductId = result.Account.Credit.Id,
                    FormName = result.Account.Standard.Name,
                    CustomerName = result.Marker + " Other product",
                    UserEmail = "other@example.test",
                    SubmissionDataJson = "{}"
                });
                await db.SaveChangesAsync();
                result.HistoricalId = historical.Id;
                result._ids.AddRange(await db.ProductFormSubmissions.Where(item => item.CatalogProductId == result.Account.Standard.Id ||
                    item.CatalogProductId == result.Account.Credit.Id || item.Id == historical.Id).Select(item => item.Id).ToListAsync());
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = LocalTestApplication.CreateDbContext();
            try
            {
                await db.CatalogCheckoutMails.Where(item => _mailKeys.Contains(item.StripeSessionId)).ExecuteDeleteAsync();
                await db.ProductFormSubmissions.Where(item => _ids.Contains(item.Id) || item.CatalogProductId == Account.Standard.Id ||
                    item.CatalogProductId == Account.Credit.Id || item.FormName == HistoricalName).ExecuteDeleteAsync();
            }
            finally { await Account.DisposeAsync(); }
        }
    }
}
