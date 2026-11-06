using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

namespace iD_Develops.E2ETests;

public sealed class UnifiedAppointmentBookingTests : Microsoft.Playwright.Xunit.PageTest
{
    private const string Path = "/calendar/book-private-session";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StudentBooksWithProgressAndCancelsReturningCreditsAndReleasingTheSlot(bool mobile)
    {
        await using var fixture = await BookingFixture.CreateAsync();
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
        await LoginAsync(Page, fixture.Student);
        var slot = await ChooseAsync(Page, fixture, fixture.Teacher.User.Id, fixture.StartUtc);
        await Page.Locator("[data-appointment-slots] section").First.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, mobile ? "booking-slots-mobile.png" : "booking-slots-desktop.png") });
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = 0;
        Task? confirmationClick = null;
        var snapshot = await WorkflowFeedbackProbe.AppointmentSubmissionAsync(Page);
        await Page.RouteAsync("**/calendar/book-private-session?handler=Book**", async route =>
        {
            Interlocked.Increment(ref posts);
            received.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await slot.ClickAsync();
            var dialog = Page.Locator("[data-app-dialog]");
            await Expect(dialog).ToContainTextAsync("2 session credits");
            await Expect(dialog).ToContainTextAsync(fixture.Student.User.FullName);
            confirmationClick = dialog.Locator("[data-app-dialog-confirm]").ClickAsync();
            await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(state.GetProperty("disabled").GetBoolean(), state.ToString());
            Assert.Equal("true", state.GetProperty("busy").GetString());
            Assert.True(state.GetProperty("spinnerVisible").GetBoolean(), state.ToString());
            Assert.True(state.GetProperty("duplicatePrevented").GetBoolean(), state.ToString());
            Assert.Equal(1, posts);
        }
        finally
        {
            release.TrySetResult();
            if (confirmationClick != null) await confirmationClick;
        }
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Your session has been booked.");
        await using var db = LocalTestApplication.CreateDbContext();
        var booking = await db.EventBookings.AsNoTracking().SingleAsync(item => item.UserId == fixture.Student.User.Id);
        Assert.Equal(fixture.Student.User.Id, booking.BookedByUserId);
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
        Assert.Equal(1, posts);
        var card = Page.Locator($"[data-booking-event-id='{booking.ScheduledEventId}']");
        await Expect(card).ToContainTextAsync("Confirmed");
        await Expect(Page.Locator("[data-appointment-type]")).ToBeEnabledAsync();
        await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, mobile ? "unified-booking-mobile.png" : "unified-booking-desktop.png"), FullPage = true });
        await card.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, mobile ? "booking-card-mobile.png" : "booking-card-desktop.png") });
        Assert.True(await Page.Locator("[data-appointment-booking]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await card.GetByRole(AriaRole.Button, new() { Name = "Cancel session", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-app-dialog]")).ToContainTextAsync("credits will be returned");
        await Page.Locator("[data-app-dialog-confirm]").ClickAsync();
        await Expect(card).ToContainTextAsync("Cancelled");
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Student.User.Id));
        slot = await ChooseAsync(Page, fixture, fixture.Teacher.User.Id, fixture.StartUtc);
        await Expect(slot).ToBeEnabledAsync();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Teacher")]
    public async Task StaffCanBookForAStudentOrThemselvesWithoutDebitingTheWrongAccount(string role)
    {
        await using var fixture = await BookingFixture.CreateAsync();
        var actor = role == "Admin" ? fixture.Admin : fixture.Teacher;
        await LoginAsync(Page, actor);
        await Page.GetByLabel("Who is attending?").SelectOptionAsync(fixture.Student.User.Id);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Use attendee", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-attendee-summary]")).ToContainTextAsync(fixture.Student.User.FullName);
        var host = role == "Admin" ? fixture.OtherTeacher.User.Id : fixture.Teacher.User.Id;
        var slot = await ChooseAsync(Page, fixture, host, fixture.StartUtc);
        if (role == "Teacher") Assert.DoesNotContain(fixture.OtherTeacher.User.Id, await Page.Locator("[data-appointment-teacher] option").EvaluateAllAsync<string[]>("items => items.map(item => item.value)"));
        await slot.ClickAsync();
        await Page.Locator("[data-app-dialog-confirm]").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Session booked for");
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
        Assert.Equal(3, await fixture.BalanceAsync(actor.User.Id));
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.Equal(actor.User.Id, (await db.EventBookings.SingleAsync(item => item.UserId == fixture.Student.User.Id)).BookedByUserId);
        await Page.GetByLabel("Who is attending?").SelectOptionAsync(actor.User.Id);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Use attendee", Exact = true }).ClickAsync();
        host = role == "Admin" ? fixture.Teacher.User.Id : fixture.OtherTeacher.User.Id;
        slot = await ChooseAsync(Page, fixture, host, fixture.StartUtc.AddHours(2));
        await slot.ClickAsync();
        await Page.Locator("[data-app-dialog-confirm]").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Your session has been booked.");
        Assert.Equal(1, await fixture.BalanceAsync(actor.User.Id));
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    public async Task ForgedAttendeeOrHostCannotBypassBookingOwnership(string role)
    {
        await using var fixture = await BookingFixture.CreateAsync();
        await LoginAsync(Page, role == "Student" ? fixture.Student : fixture.Teacher);
        var response = await PostAsync(Page, fixture, fixture.Student.User.Id, fixture.OtherTeacher.User.Id, fixture.StartUtc, Guid.NewGuid(), role == "Student" ? fixture.Admin.User.Id : fixture.Student.User.Id);
        if (role == "Student") Assert.True(response.Status == 403 || response.Url.Contains("access-denied", StringComparison.Ordinal));
        else Assert.True(response.Body.Contains("Teachers can book for students only with themselves", StringComparison.Ordinal), $"Unexpected booking response: HTTP {response.Status}.");
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.False(await db.AppointmentBookingRequests.AnyAsync(item => item.RequestedByUserId == fixture.Student.User.Id || item.RequestedByUserId == fixture.Teacher.User.Id));
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Student.User.Id));
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Admin.User.Id));
    }

    [Fact]
    public async Task DuplicatePostsCreateOneBookingAndOneCreditReservation()
    {
        await using var fixture = await BookingFixture.CreateAsync();
        await LoginAsync(Page, fixture.Student);
        var nonce = Guid.NewGuid();
        var results = await Task.WhenAll(PostAsync(Page, fixture, fixture.Student.User.Id, fixture.Teacher.User.Id, fixture.StartUtc, nonce),
            PostAsync(Page, fixture, fixture.Student.User.Id, fixture.Teacher.User.Id, fixture.StartUtc, nonce));
        Assert.All(results, item => Assert.Equal(200, item.Status));
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.Single(await db.AppointmentBookingRequests.Where(item => item.Id == nonce).ToListAsync());
        Assert.Single(await db.EventBookings.Where(item => item.UserId == fixture.Student.User.Id).ToListAsync());
        Assert.Single(await db.UserCreditTransactions.Where(item => item.UserId == fixture.Student.User.Id && item.TransactionType == CreditTransactionType.Reserve).ToListAsync());
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
    }

    [Fact]
    public async Task CompetingStudentsCannotBookOverlappingTimesForTheSameTeacher()
    {
        await using var fixture = await BookingFixture.CreateAsync();
        var otherStudent = await fixture.AddStudentAsync();
        await LoginAsync(Page, fixture.Student);
        await using var otherContext = await Browser.NewContextAsync();
        var otherPage = await otherContext.NewPageAsync();
        await LoginAsync(otherPage, otherStudent);
        var results = await Task.WhenAll(PostAsync(Page, fixture, fixture.Student.User.Id, fixture.Teacher.User.Id, fixture.StartUtc, Guid.NewGuid()),
            PostAsync(otherPage, fixture, otherStudent.User.Id, fixture.Teacher.User.Id, fixture.StartUtc.AddMinutes(30), Guid.NewGuid()));
        Assert.All(results, item => Assert.Equal(200, item.Status));
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.Single(await db.ScheduledEvents.Where(item => item.TeacherUserId == fixture.Teacher.User.Id).ToListAsync());
        Assert.Equal(4, await fixture.BalanceAsync(fixture.Student.User.Id) + await fixture.BalanceAsync(otherStudent.User.Id));
    }

    [Fact]
    public async Task CompetingBookingsForOneAttendeeCannotOverspendTheirCredits()
    {
        await using var fixture = await BookingFixture.CreateAsync();
        await LoginAsync(Page, fixture.Student);
        var results = await Task.WhenAll(PostAsync(Page, fixture, fixture.Student.User.Id, fixture.Teacher.User.Id, fixture.StartUtc, Guid.NewGuid()),
            PostAsync(Page, fixture, fixture.Student.User.Id, fixture.OtherTeacher.User.Id, fixture.StartUtc.AddHours(2), Guid.NewGuid()));
        Assert.All(results, item => Assert.Equal(200, item.Status));
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.Single(await db.EventBookings.Where(item => item.UserId == fixture.Student.User.Id).ToListAsync());
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
    }

    [Fact]
    public async Task PendingSetupRequiresAdministratorReviewBeforeCreditsCanBeReleased()
    {
        await using var fixture = await BookingFixture.CreateAsync();
        await LoginAsync(Page, fixture.Student);
        var nonce = Guid.NewGuid();
        Assert.Equal(200, (await PostAsync(Page, fixture, fixture.Student.User.Id, fixture.Teacher.User.Id, fixture.StartUtc, nonce)).Status);
        await using var db = LocalTestApplication.CreateDbContext();
        await db.AppointmentBookingRequests.Where(item => item.Id == nonce).ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, "ReviewRequired").SetProperty(item => item.Message, "Simulated missing provider response"));
        var forbidden = await RawPostAsync(Page, "ResolveSetup", new() { ["requestId"] = nonce.ToString(), ["providerCleanupConfirmed"] = "true" });
        Assert.True(forbidden.Status == 403 || forbidden.Url.Contains("access-denied", StringComparison.Ordinal));
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
        await Page.Context.ClearCookiesAsync();
        await LoginAsync(Page, fixture.Admin);
        var issue = Page.Locator($"[data-setup-request-id='{nonce}']");
        await Expect(issue).ToBeVisibleAsync();
        var noConfirmation = await RawPostAsync(Page, "ResolveSetup", new() { ["requestId"] = nonce.ToString() });
        Assert.Equal(200, noConfirmation.Status);
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
        await issue.Locator("summary").ClickAsync();
        await issue.GetByRole(AriaRole.Checkbox).CheckAsync();
        await issue.GetByRole(AriaRole.Button, new() { Name = "Cancel unconfirmed setup", Exact = true }).ClickAsync();
        await Page.Locator("[data-app-dialog-confirm]").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("reserved credits released");
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Student.User.Id));
        Assert.Equal("Failed", (await db.AppointmentBookingRequests.AsNoTracking().SingleAsync(item => item.Id == nonce)).Status);
        Assert.Equal(fixture.Admin.User.Id, (await db.ApplicationAuditEvents.SingleAsync(item => item.EntityId == nonce.ToString() && item.EventType == "scheduling.appointment.setup_resolved")).ActorUserId);
    }

    [Fact]
    public async Task FreeSessionCanBeBookedWithoutAnyCreditBalance()
    {
        await using var fixture = await BookingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        await db.AppointmentTypes.Where(item => item.Id == fixture.Type.Id).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.RequiredCreditTypeId, (int?)null).SetProperty(item => item.CreditConsumptionPolicyId, (int?)null).SetProperty(item => item.CreditCost, 0));
        await db.UserCreditLots.Where(item => item.UserId == fixture.Student.User.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.RemainingQuantity, 0));
        await LoginAsync(Page, fixture.Student);
        var slot = await ChooseAsync(Page, fixture, fixture.Teacher.User.Id, fixture.StartUtc);
        await Expect(Page.Locator("[data-detail-credit]")).ToHaveTextAsync("No credits required");
        await slot.ClickAsync();
        await Expect(Page.Locator("[data-app-dialog]")).ToContainTextAsync("No credits are required");
        await Page.Locator("[data-app-dialog-confirm]").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Your session has been booked.");
        Assert.False(await db.UserCreditTransactions.AnyAsync(item => item.UserId == fixture.Student.User.Id));
        Assert.Equal(0, await fixture.BalanceAsync(fixture.Student.User.Id));
    }

    [Fact]
    public async Task CancellationWaitsForItsConfirmationHandlerWhenScriptsAreSlow()
    {
        await using var fixture = await BookingFixture.CreateAsync();
        await LoginAsync(Page, fixture.Student);
        Assert.Equal(200, (await PostAsync(Page, fixture, fixture.Student.User.Id, fixture.Teacher.User.Id, fixture.StartUtc, Guid.NewGuid())).Status);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/js/appointment-booking.js*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}{Path}", new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Cancel session", Exact = true })).ToBeDisabledAsync();
            Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
        }
        finally { release.TrySetResult(); }
        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel session", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-app-dialog]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-app-dialog]")).ToContainTextAsync("credits will be returned");
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.User.Id));
        await Page.Locator("[data-app-dialog-confirm]").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Session cancelled.");
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Student.User.Id));
    }

    [Theory]
    [InlineData("Student", false)]
    [InlineData("Student", true)]
    [InlineData("Teacher", false)]
    [InlineData("Teacher", true)]
    [InlineData("Admin", false)]
    [InlineData("Admin", true)]
    public async Task CalendarActionsSeparateSessionBookingEventsAndWorkingHours(string role, bool mobile)
    {
        await using var fixture = await BookingFixture.CreateAsync();
        var account = role == "Admin" ? fixture.Admin : role == "Teacher" ? fixture.Teacher : fixture.Student;
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
        await LoginAsync(Page, account);
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/calendar");
        var actions = Page.GetByRole(AriaRole.Navigation, new() { Name = "Calendar actions" });
        await Expect(Page.Locator(".portal-nav-list a[href*='book-private-session'], .portal-nav-list a[href*='/calendar/schedule']")).ToHaveCountAsync(0);
        if (role == "Student")
        {
            await Expect(actions.GetByRole(AriaRole.Link)).ToHaveCountAsync(1);
            await actions.GetByRole(AriaRole.Link, new() { Name = "Book session", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Book session", Exact = true })).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-appointment-booking]")).ToBeVisibleAsync();
            foreach (var path in new[] { "/calendar/schedule", "/calendar/schedule?mode=single", "/calendar/schedule?mode=recurring", "/calendar/roster", "/calendar/appointment-types" })
            {
                var response = await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + path);
                Assert.True(response?.Status == 403 || Page.Url.Contains("/access-denied", StringComparison.Ordinal), $"Student could access {path}: HTTP {response?.Status}.");
            }
            return;
        }
        await Expect(actions.GetByRole(AriaRole.Link)).ToHaveCountAsync(3);
        await Expect(actions.GetByRole(AriaRole.Link, new() { Name = "Manage roster", Exact = true })).ToBeVisibleAsync();
        await Expect(actions.GetByRole(AriaRole.Link, new() { Name = "Event type", Exact = true })).ToBeVisibleAsync();
        await actions.GetByRole(AriaRole.Link, new() { Name = "Schedule event", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Schedule event", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-appointment-booking]")).ToBeVisibleAsync();
        var choices = Page.GetByRole(AriaRole.Navigation, new() { Name = "Scheduling options" });
        await choices.GetByRole(AriaRole.Link, new() { Name = "One-time event", Exact = true }).ClickAsync();
        var manual = Page.Locator(".roster-manual-form");
        await Expect(manual).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-availability-form-root], [data-roster-form]")).ToHaveCountAsync(0);
        await manual.Locator("[data-manual-duration]").FillAsync("0");
        await manual.EvaluateAsync("form => { form.noValidate = true; form.requestSubmit(); }");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Enter a duration between 5 and 1440 minutes.");
        await Expect(Page.Locator(".roster-manual-form")).ToBeVisibleAsync();
        await choices.GetByRole(AriaRole.Link, new() { Name = "Recurring classes", Exact = true }).ClickAsync();
        var recurring = Page.Locator("[data-roster-form]");
        await Expect(recurring).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-availability-form-root], .roster-manual-form")).ToHaveCountAsync(0);
        await recurring.EvaluateAsync("form => { form.noValidate = true; form.requestSubmit(); }");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Select a course class.");
        await Expect(Page.Locator("[data-roster-form]")).ToBeVisibleAsync();
        Assert.True(await Page.Locator("[data-schedule-events]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, $"schedule-event-{role}-{mobile}.png"), FullPage = true });
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/calendar");
        await actions.GetByRole(AriaRole.Link, new() { Name = "Manage roster", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Manage roster", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-roster-form], .roster-manual-form, [data-appointment-booking]")).ToHaveCountAsync(0);
        var availability = Page.Locator("[data-availability-form-root]");
        var availabilityOwner = role == "Admin" ? fixture.OtherTeacher.User.Id : account.User.Id;
        if (role == "Admin")
        {
            await Expect(availability.Locator("[data-availability-teacher]")).ToHaveValueAsync(account.User.Id);
            await availability.Locator("[data-availability-teacher]").SelectOptionAsync(availabilityOwner);
        }
        var monday = availability.Locator("[data-availability-day='Monday']");
        await monday.Locator("[data-availability-enabled]").CheckAsync();
        await monday.Locator("[data-availability-start]").FillAsync("08:00");
        await monday.Locator("[data-availability-end]").FillAsync("17:00");
        await availability.GetByRole(AriaRole.Button, new() { Name = "Save availability", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Teacher availability saved.");
        await Expect(Page.Locator("[data-availability-teacher]")).ToHaveValueAsync(availabilityOwner);
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.True(await db.TeacherAvailabilityWindows.AnyAsync(item => item.TeacherUserId == availabilityOwner && item.DayOfWeek == DayOfWeek.Monday && item.LocalStartTime == new TimeOnly(8, 0) && item.LocalEndTime == new TimeOnly(17, 0)));
        if (role == "Admin") Assert.False(await db.TeacherAvailabilityWindows.AnyAsync(item => item.TeacherUserId == account.User.Id));
        Assert.False(await db.ScheduledEvents.AnyAsync(item => item.TeacherUserId == fixture.Teacher.User.Id || item.TeacherUserId == fixture.Admin.User.Id));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Teacher")]
    public async Task StaffCanCreateAOneTimeEventFromTheSchedulingPage(string role)
    {
        await using var fixture = await BookingFixture.CreateAsync();
        var actor = role == "Admin" ? fixture.Admin : fixture.Teacher;
        await LoginAsync(Page, actor);
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Scheduling options" }).GetByRole(AriaRole.Link, new() { Name = "One-time event", Exact = true }).ClickAsync();
        var form = Page.Locator(".roster-manual-form");
        await form.Locator("[data-manual-start]").FillAsync(fixture.Date.ToString("yyyy-MM-dd") + "T10:00");
        await form.Locator("[data-manual-duration]").FillAsync("45");
        if (role == "Admin")
        {
            await Expect(form.Locator($"[data-manual-teacher] option[value='{fixture.Teacher.User.Id}']")).ToBeEnabledAsync();
            await form.Locator("[data-manual-teacher]").SelectOptionAsync(fixture.Teacher.User.Id);
        }
        await form.GetByText("Advanced: custom session", new() { Exact = true }).ClickAsync();
        await form.Locator("[data-manual-student]").SelectOptionAsync(fixture.Student.User.Id);
        await form.Locator("[name='ManualInput.DeliveryType']").SelectOptionAsync("InPerson");
        await form.Locator("[name='ManualInput.Location']").FillAsync("E2E studio");
        await form.GetByRole(AriaRole.Button, new() { Name = "Schedule meeting", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Meeting scheduled.");
        await Expect(Page.Locator(".roster-manual-form")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-availability-form-root], [data-roster-form]")).ToHaveCountAsync(0);
        await using var db = LocalTestApplication.CreateDbContext();
        var meeting = await db.ScheduledEvents.SingleAsync(item => item.TeacherUserId == fixture.Teacher.User.Id);
        Assert.Equal(ScheduleDeliveryType.InPerson, meeting.DeliveryType);
        Assert.Equal(TimeSpan.FromMinutes(45), meeting.EndAtUtc - meeting.StartAtUtc);
        Assert.True(await db.EventBookings.AnyAsync(item => item.ScheduledEventId == meeting.Id && item.UserId == fixture.Student.User.Id));
    }

    private static async Task LoginAsync(IPage page, CatalogBrowsingFixture account)
    {
        await page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/login?returnUrl={Uri.EscapeDataString(Path)}");
        await page.GetByLabel("E-mail").FillAsync(account.User.Email!);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(account.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath is Path or "/calendar/schedule");
    }

    private async Task<ILocator> ChooseAsync(IPage page, BookingFixture fixture, string teacherId, DateTime start)
    {
        await page.GetByLabel("1. Event type").SelectOptionAsync(fixture.Type.Id.ToString());
        await page.GetByLabel("2. Teacher").SelectOptionAsync(teacherId);
        await page.GetByLabel("3. Starting date").FillAsync(fixture.Date.ToString("yyyy-MM-dd"));
        var slot = page.Locator($"[data-slot-start='{start:yyyy-MM-ddTHH:mm:ss}Z']");
        await Expect(slot).ToBeEnabledAsync();
        return slot;
    }

    private static Task<PostResult> PostAsync(IPage page, BookingFixture fixture, string attendeeId, string teacherId, DateTime start, Guid nonce, string? forgedAttendee = null)
        => RawPostAsync(page, "Book", new()
        {
            ["bookingRequestId"] = nonce.ToString(),
            ["appointmentTypeId"] = fixture.Type.Id.ToString(),
            ["teacherUserId"] = teacherId,
            ["startUtc"] = start.ToString("O"),
            ["attendeeUserId"] = forgedAttendee ?? attendeeId
        });

    private static async Task<PostResult> RawPostAsync(IPage page, string handler, Dictionary<string, string> fields)
    {
        var result = await page.EvaluateAsync<JsonElement>("""
            async args => {
                const token = document.querySelector('[data-appointment-book-form] [name=__RequestVerificationToken]').value;
                const body = new URLSearchParams({...args.fields, __RequestVerificationToken: token});
                const response = await fetch(location.pathname + '?handler=' + args.handler, {method:'POST', body, headers:{'X-Requested-With':'XMLHttpRequest'}});
                return {status:response.status, url:response.url, body:await response.text()};
            }
            """, new { handler, fields });
        return new(result.GetProperty("status").GetInt32(), result.GetProperty("url").GetString()!, result.GetProperty("body").GetString()!);
    }

    private sealed record PostResult(int Status, string Url, string Body);

    private sealed class BookingFixture : IAsyncDisposable
    {
        private readonly List<CatalogBrowsingFixture> _accounts = [];
        public CatalogBrowsingFixture Admin { get; private set; } = null!;
        public CatalogBrowsingFixture Teacher { get; private set; } = null!;
        public CatalogBrowsingFixture OtherTeacher { get; private set; } = null!;
        public CatalogBrowsingFixture Student { get; private set; } = null!;
        public AppointmentType Type { get; private set; } = null!;
        private CreditType Credit { get; set; } = null!;
        private CreditConsumptionPolicy Policy { get; set; } = null!;
        public DateOnly Date { get; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        public DateTime StartUtc => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(Date.ToDateTime(new(9, 0)), DateTimeKind.Unspecified), Zone());
        private static TimeZoneInfo Zone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
        }

        public static async Task<BookingFixture> CreateAsync()
        {
            var fixture = new BookingFixture();
            try
            {
                fixture.Admin = await fixture.AddAccountAsync("Booking Admin", "Admin");
                fixture.Teacher = await fixture.AddAccountAsync("Booking Teacher", "Teacher");
                fixture.OtherTeacher = await fixture.AddAccountAsync("Other Teacher", "Teacher");
                fixture.Student = await fixture.AddAccountAsync("Booking Student", "Student");
                await using var db = LocalTestApplication.CreateDbContext();
                var name = "E2E appointment " + Guid.NewGuid().ToString("N");
                fixture.Credit = new() { Name = name, NormalizedName = name.ToUpperInvariant(), SingularLabel = "session credit", PluralLabel = "session credits" };
                fixture.Policy = new() { Name = name, NormalizedName = name.ToUpperInvariant(), CancellationWindowHours = 1, EarlyCancellationAction = CreditResolutionAction.Return, StaffCancellationAction = CreditResolutionAction.Return };
                db.CreditTypes.Add(fixture.Credit); db.CreditConsumptionPolicies.Add(fixture.Policy);
                await db.SaveChangesAsync();
                fixture.Type = new()
                {
                    Name = name,
                    CreatedByUserId = fixture.Admin.User.Id,
                    DurationMinutes = 60,
                    DeliveryType = ScheduleDeliveryType.InPerson,
                    Location = "E2E studio",
                    RequiredCreditTypeId = fixture.Credit.Id,
                    CreditCost = 2,
                    CreditConsumptionPolicyId = fixture.Policy.Id,
                    Teachers = [new() { TeacherUserId = fixture.Teacher.User.Id }, new() { TeacherUserId = fixture.OtherTeacher.User.Id }]
                };
                db.AppointmentTypes.Add(fixture.Type);
                foreach (var host in new[] { fixture.Teacher, fixture.OtherTeacher }) db.TeacherAvailabilityWindows.Add(new() { TeacherUserId = host.User.Id, DayOfWeek = fixture.Date.DayOfWeek, LocalStartTime = new(8, 0), LocalEndTime = new(18, 0) });
                foreach (var account in fixture._accounts) db.UserCreditLots.Add(fixture.Lot(account.User.Id));
                await db.SaveChangesAsync();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task<CatalogBrowsingFixture> AddStudentAsync()
        {
            var account = await AddAccountAsync("Other Student", "Student");
            await using var db = LocalTestApplication.CreateDbContext();
            db.UserCreditLots.Add(Lot(account.User.Id));
            await db.SaveChangesAsync();
            return account;
        }

        private async Task<CatalogBrowsingFixture> AddAccountAsync(string name, string role)
        {
            var account = await CatalogBrowsingFixture.CreateAsync(administrator: role == "Admin");
            _accounts.Add(account);
            await using var db = LocalTestApplication.CreateDbContext();
            var user = await db.Users.SingleAsync(item => item.Id == account.User.Id);
            user.FirstName = name; user.LastName = "Fixture";
            account.User.FirstName = name; account.User.LastName = "Fixture";
            if (role == "Teacher")
            {
                await db.UserRoles.Where(item => item.UserId == user.Id).ExecuteDeleteAsync();
                var teacherRole = await db.Roles.SingleAsync(item => item.NormalizedName == "TEACHER");
                db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = teacherRole.Id });
            }
            await db.SaveChangesAsync();
            return account;
        }

        private UserCreditLot Lot(string userId) => new() { UserId = userId, CreditTypeId = Credit.Id, GrantedQuantity = 3, RemainingQuantity = 3, Scope = CreditGrantScope.Global };
        public async Task<int> BalanceAsync(string id)
        {
            await using var db = LocalTestApplication.CreateDbContext();
            return await db.UserCreditLots.Where(item => item.UserId == id && item.CreditTypeId == Credit.Id).SumAsync(item => item.RemainingQuantity);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var db = LocalTestApplication.CreateDbContext();
                var users = _accounts.Select(item => item.User.Id).ToArray();
                var eventIds = await db.ScheduledEvents.Where(item => users.Contains(item.TeacherUserId)).Select(item => item.Id).ToArrayAsync();
                var bookingIds = await db.EventBookings.Where(item => eventIds.Contains(item.ScheduledEventId)).Select(item => item.Id).ToArrayAsync();
                await db.AppointmentBookingRequests.Where(item => users.Contains(item.RequestedByUserId) || users.Contains(item.AttendeeUserId) || users.Contains(item.TeacherUserId)).ExecuteDeleteAsync();
                await db.UserCreditTransactions.Where(item => users.Contains(item.UserId)).ExecuteDeleteAsync();
                await db.EventBookingCreditAllocations.Where(item => bookingIds.Contains(item.EventBookingId)).ExecuteDeleteAsync();
                await db.EventBookings.Where(item => bookingIds.Contains(item.Id)).ExecuteDeleteAsync();
                await db.ScheduledEvents.Where(item => eventIds.Contains(item.Id)).ExecuteDeleteAsync();
                await db.AppointmentTypes.Where(item => users.Contains(item.CreatedByUserId)).ExecuteDeleteAsync();
                await db.TeacherAvailabilityWindows.Where(item => users.Contains(item.TeacherUserId)).ExecuteDeleteAsync();
                await db.UserCreditLots.Where(item => users.Contains(item.UserId)).ExecuteDeleteAsync();
                if (Credit?.Id > 0) await db.CreditTypes.Where(item => item.Id == Credit.Id).ExecuteDeleteAsync();
                if (Policy?.Id > 0) await db.CreditConsumptionPolicies.Where(item => item.Id == Policy.Id).ExecuteDeleteAsync();
                await db.ApplicationAuditEvents.Where(item => users.Contains(item.ActorUserId!) || users.Contains(item.SubjectUserId!)).ExecuteDeleteAsync();
            }
            finally { foreach (var account in _accounts.AsEnumerable().Reverse()) await account.DisposeAsync(); }
        }
    }
}
