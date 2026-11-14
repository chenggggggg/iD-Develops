using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

namespace iD_Develops.E2ETests;

public sealed class UnifiedAppointmentBookingTests : DiagnosticPageTest
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
        Page.PageError += (_, error) => Console.WriteLine($"Calendar browser error: {error}");
        await using var fixture = await BookingFixture.CreateAsync();
        var account = role == "Admin" ? fixture.Admin : role == "Teacher" ? fixture.Teacher : fixture.Student;
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
        await LoginAsync(Page, account);
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/calendar");
        var actions = Page.GetByRole(AriaRole.Navigation, new() { Name = "Calendar actions" });
        await Expect(Page.Locator(".portal-nav-list a[href*='book-private-session'], .portal-nav-list a[href*='/calendar/schedule']")).ToHaveCountAsync(0);
        if (role == "Student")
        {
            var upcomingTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Upcoming", Exact = true });
            await Expect(upcomingTab).ToBeEnabledAsync();
            await upcomingTab.FocusAsync();
            await Expect(upcomingTab).ToBeFocusedAsync();
            await Page.Keyboard.PressAsync("ArrowRight");
            await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Available events", Exact = true })).ToHaveAttributeAsync("aria-selected", "true");
            await Page.Keyboard.PressAsync("ArrowLeft");
            await Expect(upcomingTab).ToHaveAttributeAsync("aria-selected", "true");
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
        async Task AssertCurrentPageDoesNotReloadAsync(string name)
        {
            var current = choices.Locator("[aria-current='page']");
            await Expect(current).ToHaveTextAsync(name);
            await Expect(current).ToHaveAttributeAsync("aria-disabled", "true");
            await Expect(choices.GetByRole(AriaRole.Link)).ToHaveCountAsync(2);
            var navigations = new List<string>();
            EventHandler<IRequest> observe = (_, request) =>
            {
                if (request.IsNavigationRequest && request.Frame == Page.MainFrame) navigations.Add(request.Url);
            };
            Page.Request += observe;
            try
            {
                for (var click = 0; click < 3; click++)
                    await current.ClickAsync(new() { Force = true });
                Assert.True(navigations.Count == 0, string.Join(Environment.NewLine, navigations));
            }
            finally { Page.Request -= observe; }
        }
        await AssertCurrentPageDoesNotReloadAsync("Book session");
        await choices.GetByRole(AriaRole.Link, new() { Name = "One-time event", Exact = true }).ClickAsync();
        await AssertCurrentPageDoesNotReloadAsync("One-time event");
        var manual = Page.Locator("[data-manual-form]");
        await Expect(manual).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-availability-form-root], [data-roster-form]")).ToHaveCountAsync(0);
        await manual.Locator("[data-manual-duration]").FillAsync("0");
        await manual.EvaluateAsync("form => form.submit()");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Enter a duration between 5 and 1440 minutes.");
        await Expect(Page.Locator("[data-manual-form]")).ToBeVisibleAsync();
        await manual.Locator("[data-manual-duration]").EvaluateAsync("input => { input.type = 'text'; input.value = 'invalid'; }");
        await manual.EvaluateAsync("form => form.submit()");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("invalid");
        await Expect(manual.Locator("[data-manual-duration]")).ToHaveAttributeAsync("value", "invalid");
        await choices.GetByRole(AriaRole.Link, new() { Name = "Recurring classes", Exact = true }).ClickAsync();
        await AssertCurrentPageDoesNotReloadAsync("Recurring classes");
        var recurring = Page.Locator("[data-roster-form]");
        await Expect(recurring).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-availability-form-root], [data-manual-form]")).ToHaveCountAsync(0);
        await recurring.EvaluateAsync("form => form.submit()");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The value '' is invalid.");
        await Expect(Page.Locator("[data-roster-form]")).ToBeVisibleAsync();
        Assert.True(await Page.Locator("[data-schedule-events]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, $"schedule-event-{role}-{mobile}.png"), FullPage = true });
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/calendar");
        await actions.GetByRole(AriaRole.Link, new() { Name = "Manage roster", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Manage roster", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-roster-form], [data-manual-form], [data-appointment-booking]")).ToHaveCountAsync(0);
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
        await Page.GetByRole(AriaRole.Heading, new() { Name = "Manage roster", Exact = true }).ScrollIntoViewIfNeededAsync();
        await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, $"b11-roster-{role}-{mobile}.png") });
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
        var form = Page.Locator("[data-manual-form]");
        await form.Locator("[data-manual-start]").FillAsync(fixture.Date.ToString("yyyy-MM-dd") + "T10:00");
        await form.Locator("[data-manual-duration]").FillAsync("45");
        if (role == "Admin")
        {
            await Expect(form.Locator($"[data-manual-teacher] option[value='{fixture.Teacher.User.Id}']")).ToBeEnabledAsync();
            await form.Locator("[data-manual-teacher]").SelectOptionAsync(fixture.Teacher.User.Id);
        }
        await form.GetByRole(AriaRole.Radio, new() { Name = "Student", Exact = true }).CheckAsync();
        await form.Locator("[data-manual-student]").SelectOptionAsync(fixture.Student.User.Id);
        await form.Locator("[name='ManualInput.DeliveryType']").SelectOptionAsync("InPerson");
        await form.Locator("[name='ManualInput.Location']").FillAsync("E2E studio");
        await form.GetByRole(AriaRole.Button, new() { Name = "Schedule meeting", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Meeting scheduled.");
        await Expect(Page.Locator("[data-manual-form]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-availability-form-root], [data-roster-form]")).ToHaveCountAsync(0);
        await using var db = LocalTestApplication.CreateDbContext();
        var meeting = await db.ScheduledEvents.SingleAsync(item => item.TeacherUserId == fixture.Teacher.User.Id);
        Assert.Equal(ScheduleDeliveryType.InPerson, meeting.DeliveryType);
        Assert.Equal(TimeSpan.FromMinutes(45), meeting.EndAtUtc - meeting.StartAtUtc);
        Assert.True(await db.EventBookings.AnyAsync(item => item.ScheduledEventId == meeting.Id && item.UserId == fixture.Student.User.Id));
        // A rejected overlapping save must keep the attendee, time and advanced settings.
        form = Page.Locator("[data-manual-form]");
        await form.GetByRole(AriaRole.Radio, new() { Name = "Student", Exact = true }).CheckAsync();
        await form.Locator("[data-manual-student]").SelectOptionAsync(fixture.Student.User.Id);
        await form.Locator("[data-manual-start]").FillAsync(fixture.Date.ToString("yyyy-MM-dd") + "T11:00");
        if (role == "Admin")
        {
            await Expect(form.Locator($"[data-manual-teacher] option[value='{fixture.Teacher.User.Id}']")).ToBeEnabledAsync();
            await form.Locator("[data-manual-teacher]").SelectOptionAsync(fixture.Teacher.User.Id);
            await Expect(form.Locator("[data-manual-availability-hint]")).Not.ToContainTextAsync("Checking");
        }
        await form.Locator("[name='ManualInput.DeliveryType']").SelectOptionAsync("InPerson");
        await form.Locator("[name='ManualInput.Location']").FillAsync("Keep this location");
        await form.Locator("[data-schedule-advanced] summary").ClickAsync();
        await form.Locator("[name='ManualInput.BookingOpenDaysBefore']").FillAsync("17");
        // The teacher becomes busy after selection, so the server must reject this race.
        meeting.StartAtUtc = meeting.StartAtUtc.AddHours(1);
        meeting.EndAtUtc = meeting.EndAtUtc.AddHours(1);
        await db.SaveChangesAsync();
        await form.GetByRole(AriaRole.Button, new() { Name = "Schedule meeting", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("overlaps");
        await Expect(form.Locator("[data-manual-student]")).ToHaveValueAsync(fixture.Student.User.Id);
        await Expect(form.Locator("[data-manual-start]")).ToHaveValueAsync(fixture.Date.ToString("yyyy-MM-dd") + "T11:00");
        if (role == "Admin") await Expect(form.Locator("[data-manual-teacher]")).ToHaveValueAsync(fixture.Teacher.User.Id);
        await Expect(form.Locator("[name='ManualInput.Location']")).ToHaveValueAsync("Keep this location");
        await Expect(form.Locator("[name='ManualInput.BookingOpenDaysBefore']")).ToHaveValueAsync("17");
        Assert.Equal(1, await db.ScheduledEvents.CountAsync(item => item.TeacherUserId == fixture.Teacher.User.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecurringEditorPreservesAdvancedSettingsAndSupportsKeyboardEditing(bool mobile)
    {
        await using var fixture = await BookingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var lesson = new CourseClass { Title = $"Roster UX {Guid.NewGuid():N}", DurationMinutes = 45, Capacity = 4 };
        var course = new Course { Name = "Roster UX course", CreatedByUserId = fixture.Admin.User.Id, Sections = [new CourseSection { Title = "Classes", Classes = [lesson] }] };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        try
        {
            await Page.SetViewportSizeAsync(mobile ? 390 : 1440, 1000);
            await LoginAsync(Page, fixture.Admin);
            await Page.GetByRole(AriaRole.Navigation, new() { Name = "Scheduling options" }).GetByRole(AriaRole.Link, new() { Name = "Recurring classes", Exact = true }).ClickAsync();
            var form = Page.Locator("[data-roster-form]");
            async Task FillScheduleAsync()
            {
                await form.Locator("[data-roster-class]").SelectOptionAsync(lesson.Id.ToString());
                await form.Locator("[data-roster-day]").SelectOptionAsync(fixture.Date.DayOfWeek.ToString());
                await form.Locator("[data-roster-start]").FillAsync("10:00");
                await form.Locator("[data-roster-teacher]").SelectOptionAsync(fixture.Teacher.User.Id);
                await form.Locator("[data-roster-field='ActiveFromDate']").FillAsync(fixture.Date.ToString("yyyy-MM-dd"));
                await form.Locator("[data-roster-field='ActiveUntilDate']").FillAsync(fixture.Date.AddDays(14).ToString("yyyy-MM-dd"));
                await form.Locator("[data-delivery]").SelectOptionAsync("InPerson");
                await form.Locator("[data-location]").FillAsync("Studio A");
            }
            await FillScheduleAsync();
            await Expect(form.Locator("[data-class-summary]")).ToContainTextAsync("45 minutes");
            await Expect(form.Locator("[data-class-summary]")).ToContainTextAsync("4 seats");
            var advanced = form.Locator("[data-schedule-advanced]");
            Assert.False(await advanced.EvaluateAsync<bool>("element => element.open"));
            await advanced.Locator("summary").ClickAsync();
            await form.Locator("[data-roster-field='BookingOpenDaysBefore']").FillAsync("-1");
            await advanced.EvaluateAsync("element => element.open = false");
            await form.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
            await Expect(form.Locator("[data-roster-field='BookingOpenDaysBefore']")).ToBeFocusedAsync();
            Assert.True(await advanced.EvaluateAsync<bool>("element => element.open"));
            Assert.False(await db.ScheduleRosterRules.AnyAsync(item => item.CourseClassId == lesson.Id));
            await form.Locator("[data-roster-field='BookingOpenDaysBefore']").FillAsync("17");
            await form.Locator("[data-roster-field='BookingCloseHoursBefore']").FillAsync("2");
            await form.Locator("[data-roster-field='GenerateWeeksAhead']").SelectOptionAsync("4");
            Assert.True(await Page.Locator("[data-schedule-events]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
            var fontSize = await form.Locator("[data-roster-class]").EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).fontSize)");
            Assert.True(fontSize >= (mobile ? 16 : 14));
            if (!mobile)
            {
                var editor = await Page.Locator("[data-roster-editor]").BoundingBoxAsync();
                Assert.True(editor!.Width >= 500, $"Editor is only {editor.Width}px wide.");
            }
            await Page.Locator("[data-roster-editor]").ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, $"b11-recurring-{mobile}.png") });
            await form.GetByRole(AriaRole.Group, new() { Name = "Class and teacher", Exact = true }).ScrollIntoViewIfNeededAsync();
            await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, $"b11-recurring-core-{mobile}.png") });
            await advanced.ScrollIntoViewIfNeededAsync();
            await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, $"b11-recurring-advanced-{mobile}.png") });
            var edit = Page.GetByRole(AriaRole.Button, new() { Name = $"Edit {lesson.Title}", Exact = true });
            // A redirected page can render its success message before the editor script loads.
            const string editorScript = "**/js/schedule-roster.js?*";
            var scriptRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseScript = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await Page.RouteAsync(editorScript, async route =>
            {
                scriptRequested.TrySetResult();
                await releaseScript.Task;
                await route.ContinueAsync();
            });
            var save = form.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
            try
            {
                await scriptRequested.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await Expect(Page.GetByText("Recurring class created.", new() { Exact = true })).ToBeVisibleAsync();
                await Expect(edit).ToBeDisabledAsync();
                await Expect(Page.Locator("[data-roster-new]")).ToBeDisabledAsync();
                await Expect(Page.Locator("[data-roster-reset]")).ToBeDisabledAsync();
            }
            finally
            {
                releaseScript.TrySetResult();
                await save;
                // Keep this page-scoped handler until context disposal, so cleanup
                // cannot race delivery/execution of the script. Later requests pass
                // straight through the already-released gate.
            }
            var rule = await db.ScheduleRosterRules.AsNoTracking().SingleAsync(item => item.CourseClassId == lesson.Id);
            await Expect(edit).ToBeEnabledAsync();
            await Expect(Page.Locator("[data-roster-new]")).ToBeEnabledAsync();
            await Expect(Page.Locator("[data-roster-reset]")).ToBeEnabledAsync();
            await edit.PressAsync("Enter");
            await Expect(form.Locator("[data-roster-teacher]")).ToBeFocusedAsync();
            await Expect(form.Locator("[data-roster-field='Id']")).ToHaveValueAsync(rule.Id.ToString());
            await Expect(form.Locator("[data-roster-field='BookingOpenDaysBefore']")).ToHaveValueAsync("17");
            await Expect(form.Locator("[data-roster-field='BookingCloseHoursBefore']")).ToHaveValueAsync("2");
            await form.Locator("[data-location]").FillAsync("Studio B");
            await form.GetByRole(AriaRole.Button, new() { Name = "Update recurring class", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("Recurring class updated.", new() { Exact = true })).ToBeVisibleAsync();
            Assert.Equal("Studio B", (await db.ScheduleRosterRules.AsNoTracking().SingleAsync(item => item.Id == rule.Id)).Location);
            await FillScheduleAsync();
            await advanced.Locator("summary").ClickAsync();
            await form.Locator("[data-roster-field='BookingOpenDaysBefore']").FillAsync("9");
            await form.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("overlaps");
            await Expect(form.Locator("[data-roster-class]")).ToHaveValueAsync(lesson.Id.ToString());
            await Expect(form.Locator("[data-roster-teacher]")).ToHaveValueAsync(fixture.Teacher.User.Id);
            await Expect(form.Locator("[data-roster-field='BookingOpenDaysBefore']")).ToHaveValueAsync("9");
            Assert.Equal(1, await db.ScheduleRosterRules.CountAsync(item => item.CourseClassId == lesson.Id));
        }
        finally
        {
            // Fixtures have no external meetings; remove generated unbooked occurrences before their rule/course.
            await db.ScheduledEvents.Where(item => item.CourseClassId == lesson.Id).ExecuteDeleteAsync();
            await db.ScheduleRosterRules.Where(item => item.CourseClassId == lesson.Id).ExecuteDeleteAsync();
            await db.Courses.Where(item => item.Id == course.Id).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("2027-01-10T12:00:00Z", "CET")]
    [InlineData("2027-06-15T12:00:00Z", "CEST")]
    [InlineData("2027-03-28T12:00:00Z", "CET/CEST")]
    public async Task CalendarShowsAmsterdamZoneForTheDisplayedWeek(string date, string label)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await LoginAsync(Page, account);
        await Page.SetViewportSizeAsync(1440, 1000);
        await Page.Clock.SetFixedTimeAsync(DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal));
        await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}/calendar");
        await Expect(Page.Locator("[data-schedule-zone-label]")).ToHaveTextAsync($"Amsterdam ({label})");
        Assert.True(await Page.Locator("[data-schedule-root]").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(AppContext.BaseDirectory, $"b11-calendar-{label.Replace('/', '-')}.png"), FullPage = true });
    }

    private static async Task LoginAsync(IPage page, CatalogBrowsingFixture account)
    {
        await BrowserLogin.SignInAsync(page, account.User.Email!, account.Password, Path,
            page.GetByLabel("1. Event type"), url =>
                new Uri(url).GetLeftPart(UriPartial.Authority) == LocalTestApplication.PortalBaseUrl &&
                new Uri(url).AbsolutePath is Path or "/calendar/schedule");
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
