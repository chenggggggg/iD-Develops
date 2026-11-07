using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class EventTypeManagementTests : DiagnosticPageTest
{
    private const string ManagementPath = "/calendar/appointment-types";

    [Fact]
    public async Task LoginWaitsForUsablePageWhileAnUnrelatedImageIsStillLoading()
    {
        await using var fixture = await EventTypeFixture.CreateAsync("Admin");
        const string imageUrl = "https://e2e-delayed.example.test/image.svg";
        var imageRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseImage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync(imageUrl, async route =>
        {
            imageRequested.TrySetResult();
            await releaseImage.Task;
            await route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>" });
        });
        await Page.AddInitScriptAsync("""
            if (location.pathname === '/calendar/appointment-types') {
                document.addEventListener('DOMContentLoaded', () => {
                    const image = document.createElement('img');
                    image.src = 'https://e2e-delayed.example.test/image.svg';
                    image.alt = '';
                    document.body.append(image);
                }, {once: true});
            }
            """);
        try
        {
            await LoginAsync(fixture.Account, ManagementPath);
            await imageRequested.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Expect(Page.GetByLabel("Name", new() { Exact = true })).ToBeEditableAsync();
            Assert.Equal("interactive", await Page.EvaluateAsync<string>("document.readyState"));
        }
        finally
        {
            releaseImage.TrySetResult();
            await Page.WaitForLoadStateAsync(LoadState.Load);
            await Page.UnrouteAsync(imageUrl);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdministratorCanCreateEditAndDeactivateFreeAndPaidTypes(bool mobile)
    {
        await using var fixture = await EventTypeFixture.CreateAsync("Admin");
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
        await LoginAsync(fixture.Account, ManagementPath);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Event types", Exact = true })).ToBeVisibleAsync();
        await Page.GetByLabel("Name", new() { Exact = true }).FillAsync(fixture.Name);
        await Page.GetByLabel("Duration (minutes)").FillAsync("25");
        await Page.Locator($"[data-teacher-id='{fixture.Teacher.User.Id}']").CheckAsync();
        await Expect(Page.GetByLabel("Required credit", new() { Exact = true })).Not.ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save event type", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Event type created.");
        await using var db = LocalTestApplication.CreateDbContext();
        var saved = await db.AppointmentTypes.AsNoTracking().SingleAsync(item => item.CreatedByUserId == fixture.Account.User.Id);
        var card = Page.Locator($"[data-event-type-id='{saved.Id}']");
        await Expect(card).ToContainTextAsync("25 minutes");
        await Expect(card).ToContainTextAsync("No credits required");
        Assert.Null(saved.RequiredCreditTypeId);
        Assert.Null(saved.CreditConsumptionPolicyId);
        Assert.Equal(0, saved.CreditCost);

        await card.GetByRole(AriaRole.Button, new() { Name = $"Edit {fixture.Name}", Exact = true }).ClickAsync();
        await Page.GetByLabel("Require credits to book").CheckAsync();
        await Page.GetByLabel("Required credit", new() { Exact = true }).SelectOptionAsync(fixture.Credit.Id.ToString());
        await Page.GetByLabel("Credit cost", new() { Exact = true }).FillAsync("2");
        await Page.GetByLabel("Credit policy", new() { Exact = true }).SelectOptionAsync(fixture.Policy.Id.ToString());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save event type", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Event type updated.");
        await Expect(card).ToContainTextAsync("2 session credits");
        saved = await db.AppointmentTypes.AsNoTracking().SingleAsync(item => item.Id == saved.Id);
        Assert.Equal(fixture.Credit.Id, saved.RequiredCreditTypeId);
        Assert.Equal(fixture.Policy.Id, saved.CreditConsumptionPolicyId);
        Assert.Equal(fixture.Teacher.User.Id, (await db.AppointmentTypeTeachers.SingleAsync(item => item.AppointmentTypeId == saved.Id)).TeacherUserId);

        await card.GetByRole(AriaRole.Button, new() { Name = $"Edit {fixture.Name}", Exact = true }).ClickAsync();
        await Page.GetByLabel("Require credits to book").UncheckAsync();
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, mobile ? "event-types-mobile.png" : "event-types-desktop.png"), FullPage = true });
        Assert.True(await Page.Locator("[data-appointment-admin]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save event type", Exact = true }).ClickAsync();
        await Expect(card).ToContainTextAsync("No credits required");
        saved = await db.AppointmentTypes.AsNoTracking().SingleAsync(item => item.Id == saved.Id);
        Assert.Null(saved.RequiredCreditTypeId);
        Assert.Null(saved.CreditConsumptionPolicyId);
        Assert.Equal(0, saved.CreditCost);
        await card.GetByRole(AriaRole.Button, new() { Name = $"Deactivate {fixture.Name}", Exact = true }).ClickAsync();
        await Expect(card).ToContainTextAsync("Inactive");
        Assert.False((await db.AppointmentTypes.AsNoTracking().SingleAsync(item => item.Id == saved.Id)).IsActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingCreditPolicyShowsFieldFeedbackAndPreventsClientSubmission(bool mobile)
    {
        await using var fixture = await EventTypeFixture.CreateAsync("Admin");
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
        await LoginAsync(fixture.Account, ManagementPath);
        await FillPaidSettingsWithoutPolicyAsync(fixture);
        var form = Page.Locator("[data-appointment-editor] form");
        // jQuery owns validation once it installs this attribute on the form.
        await Expect(form).ToHaveAttributeAsync("novalidate", "novalidate");
        var posts = 0;
        Page.Request += (_, request) =>
        {
            if (request.Method == "POST" && new Uri(request.Url).AbsolutePath == ManagementPath)
                Interlocked.Increment(ref posts);
        };
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save event type", Exact = true }).ClickAsync();
        var error = Page.Locator("#event-type-credit-policy-error");
        await Expect(error).ToHaveTextAsync("Select a credit policy.");
        await Expect(error).ToBeVisibleAsync();
        Assert.Equal(0, posts);
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.AppointmentTypes.AnyAsync(item => item.CreatedByUserId == fixture.Account.User.Id));
        await Page.Locator("[data-appointment-editor]").ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"event-type-policy-error-{mobile}.png") });
        await Page.GetByLabel("Credit policy", new() { Exact = true }).SelectOptionAsync(fixture.Policy.Id.ToString());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save event type", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Event type created.");
        Assert.Equal(1, posts);
        Assert.Equal(1, await db.AppointmentTypes.CountAsync(item => item.CreatedByUserId == fixture.Account.User.Id));
    }

    [Fact]
    public async Task InvalidPaidSettingsPreserveEditorValuesAndTeacherSelection()
    {
        await using var fixture = await EventTypeFixture.CreateAsync("Admin");
        await LoginAsync(fixture.Account, ManagementPath);
        await FillPaidSettingsWithoutPolicyAsync(fixture);
        // Exercise server validation independently of the separately tested client checks.
        // Native submit sends the real authenticated form, including its antiforgery token.
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.Locator("[data-appointment-editor] form").EvaluateAsync("form => HTMLFormElement.prototype.submit.call(form)"),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == ManagementPath);
        Assert.Equal(200, response.Status);
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Select a credit policy.");
        await Expect(Page.Locator("#event-type-credit-policy-error")).ToHaveTextAsync("Select a credit policy.");
        await Expect(Page.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync(fixture.Name);
        await Expect(Page.GetByLabel("Duration (minutes)")).ToHaveValueAsync("35");
        await Expect(Page.GetByLabel("Require credits to book")).ToBeCheckedAsync();
        await Expect(Page.GetByLabel("Credit cost", new() { Exact = true })).ToHaveValueAsync("3");
        await Expect(Page.GetByLabel("Required credit", new() { Exact = true })).ToHaveValueAsync(fixture.Credit.Id.ToString());
        await Expect(Page.Locator($"[data-teacher-id='{fixture.Teacher.User.Id}']")).ToBeCheckedAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.AppointmentTypes.AnyAsync(item => item.CreatedByUserId == fixture.Account.User.Id));
        await Page.GetByLabel("Credit policy", new() { Exact = true }).SelectOptionAsync(fixture.Policy.Id.ToString());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save event type", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Event type created.");
        var saved = await db.AppointmentTypes.SingleAsync(item => item.CreatedByUserId == fixture.Account.User.Id);
        Assert.Equal(fixture.Policy.Id, saved.CreditConsumptionPolicyId);
        Assert.Equal(fixture.Teacher.User.Id, (await db.AppointmentTypeTeachers.SingleAsync(item => item.AppointmentTypeId == saved.Id)).TeacherUserId);
    }

    private async Task FillPaidSettingsWithoutPolicyAsync(EventTypeFixture fixture)
    {
        await Page.GetByLabel("Name", new() { Exact = true }).FillAsync(fixture.Name);
        await Page.GetByLabel("Duration (minutes)").FillAsync("35");
        await Page.Locator($"[data-teacher-id='{fixture.Teacher.User.Id}']").CheckAsync();
        await Page.GetByLabel("Require credits to book").CheckAsync();
        await Page.GetByLabel("Required credit", new() { Exact = true }).SelectOptionAsync(fixture.Credit.Id.ToString());
        await Page.GetByLabel("Credit cost", new() { Exact = true }).FillAsync("3");
    }

    [Fact]
    public async Task TeacherCanManageOwnTypeButCannotEditOrDeactivateAnotherOwnersType()
    {
        await using var fixture = await EventTypeFixture.CreateAsync("Teacher");
        var foreign = await fixture.SeedTypeAsync("Other owner's event", paid: false, otherOwner: true);
        await LoginAsync(fixture.Account, ManagementPath);
        await Expect(Page.Locator("[data-teacher-id]")).ToHaveCountAsync(0);
        await Expect(Page.Locator($"[data-event-type-id='{foreign.Id}']")).ToHaveCountAsync(0);
        await Page.GetByLabel("Name", new() { Exact = true }).FillAsync(fixture.Name);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save event type", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Event type created.");
        await using var db = LocalTestApplication.CreateDbContext();
        var own = await db.AppointmentTypes.SingleAsync(item => item.CreatedByUserId == fixture.Account.User.Id);
        Assert.Equal(fixture.Account.User.Id, (await db.AppointmentTypeTeachers.SingleAsync(item => item.AppointmentTypeId == own.Id)).TeacherUserId);
        await Page.GetByLabel("Name", new() { Exact = true }).FillAsync("Unauthorized change");
        await Page.Locator("[data-field='id']").EvaluateAsync("(input, id) => input.value = String(id)", foreign.Id);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save event type", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("You cannot edit this event type.");
        var responseText = await Page.Locator("[data-appointment-editor] form").EvaluateAsync<string>("""
            async (form, id) => {
                const body = new FormData();
                body.set('__RequestVerificationToken', form.querySelector('[name=__RequestVerificationToken]').value);
                body.set('appointmentTypeId', String(id));
                const response = await fetch(location.pathname + '?handler=Deactivate', {method:'POST', body});
                return response.text();
            }
            """, foreign.Id);
        Assert.Contains("You cannot deactivate this event type.", responseText);
        Assert.Equal(foreign.Name, (await db.AppointmentTypes.AsNoTracking().SingleAsync(item => item.Id == foreign.Id)).Name);
        Assert.True((await db.AppointmentTypes.AsNoTracking().SingleAsync(item => item.Id == foreign.Id)).IsActive);
    }

    [Fact]
    public async Task StudentCannotOpenManagementAndSeesFreeOrPaidBookingRequirements()
    {
        var pageErrors = new List<string>();
        Page.PageError += (_, error) => pageErrors.Add(error);
        await using var fixture = await EventTypeFixture.CreateAsync("Student");
        var free = await fixture.SeedTypeAsync("Free introduction", paid: false, otherOwner: true);
        var paid = await fixture.SeedTypeAsync("Credit session", paid: true, otherOwner: true);
        await LoginAsync(fixture.Account, "/calendar");
        var scriptRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScript = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/js/appointment-booking.js*", async route =>
        {
            scriptRequested.TrySetResult();
            await releaseScript.Task;
            await route.ContinueAsync();
        });
        try
        {
            await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/calendar/book-private-session", new() { WaitUntil = WaitUntilState.Commit });
            await scriptRequested.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Expect(Page.GetByLabel("1. Event type")).ToBeDisabledAsync();
        }
        finally
        {
            releaseScript.TrySetResult();
        }
        await Page.GetByLabel("1. Event type").SelectOptionAsync(free.Id.ToString());
        await Expect(Page.Locator("[data-detail-credit]")).ToHaveTextAsync("No credits required");
        await Expect(Page.Locator("[data-detail-balance]")).Not.ToBeVisibleAsync();
        await Page.GetByLabel("1. Event type").SelectOptionAsync(paid.Id.ToString());
        await Expect(Page.Locator("[data-detail-credit]")).ToHaveTextAsync("3 session credits");
        await Expect(Page.Locator("[data-detail-balance]")).ToHaveTextAsync("Insufficient balance: 0");
        Assert.Empty(pageErrors);
        var response = await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}{ManagementPath}");
        Assert.True(response?.Status == 403 || Page.Url.Contains("/access-denied", StringComparison.Ordinal));
    }

    private async Task LoginAsync(CatalogBrowsingFixture account, string path)
    {
        await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password, path,
            path == ManagementPath
                ? Page.GetByRole(AriaRole.Heading, new() { Name = "Event types", Exact = true })
                : Page.GetByRole(AriaRole.Navigation, new() { Name = "Calendar actions" }));
    }

    private sealed class EventTypeFixture(CatalogBrowsingFixture account, CatalogBrowsingFixture teacher) : IAsyncDisposable
    {
        public CatalogBrowsingFixture Account { get; } = account;
        public CatalogBrowsingFixture Teacher { get; } = teacher;
        public string Name { get; } = $"E2E event {Guid.NewGuid():N}";
        public CreditType Credit { get; private set; } = null!;
        public CreditConsumptionPolicy Policy { get; private set; } = null!;

        public static async Task<EventTypeFixture> CreateAsync(string role)
        {
            var account = await CatalogBrowsingFixture.CreateAsync(administrator: role == "Admin");
            CatalogBrowsingFixture? teacher = null;
            EventTypeFixture? fixture = null;
            try
            {
                teacher = await CatalogBrowsingFixture.CreateAsync(administrator: true);
                fixture = new(account, teacher);
                await using var db = LocalTestApplication.CreateDbContext();
                if (role == "Teacher")
                {
                    var teacherRole = await db.Roles.SingleAsync(item => item.NormalizedName == "TEACHER");
                    db.UserRoles.Add(new IdentityUserRole<string> { UserId = account.User.Id, RoleId = teacherRole.Id });
                }
                fixture.Credit = new() { Name = fixture.Name, NormalizedName = fixture.Name.ToUpperInvariant(), SingularLabel = "session credit", PluralLabel = "session credits" };
                fixture.Policy = new() { Name = fixture.Name, NormalizedName = fixture.Name.ToUpperInvariant() };
                db.CreditTypes.Add(fixture.Credit);
                db.CreditConsumptionPolicies.Add(fixture.Policy);
                await db.SaveChangesAsync();
                return fixture;
            }
            catch
            {
                if (fixture != null) await fixture.DisposeAsync();
                else { if (teacher != null) await teacher.DisposeAsync(); await account.DisposeAsync(); }
                throw;
            }
        }

        public async Task<AppointmentType> SeedTypeAsync(string name, bool paid, bool otherOwner)
        {
            await using var db = LocalTestApplication.CreateDbContext();
            var item = new AppointmentType
            {
                Name = name,
                CreatedByUserId = otherOwner ? Teacher.User.Id : Account.User.Id,
                RequiredCreditTypeId = paid ? Credit.Id : null,
                CreditCost = paid ? 3 : 0,
                CreditConsumptionPolicyId = paid ? Policy.Id : null,
                Teachers = [new AppointmentTypeTeacher { TeacherUserId = Teacher.User.Id }]
            };
            db.AppointmentTypes.Add(item);
            await db.SaveChangesAsync();
            return item;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var db = LocalTestApplication.CreateDbContext();
                await db.AppointmentTypes.Where(item => item.CreatedByUserId == Account.User.Id || item.CreatedByUserId == Teacher.User.Id).ExecuteDeleteAsync();
                if (Credit?.Id > 0) await db.CreditTypes.Where(item => item.Id == Credit.Id).ExecuteDeleteAsync();
                if (Policy?.Id > 0) await db.CreditConsumptionPolicies.Where(item => item.Id == Policy.Id).ExecuteDeleteAsync();
            }
            finally
            {
                await Teacher.DisposeAsync();
                await Account.DisposeAsync();
            }
        }
    }
}
