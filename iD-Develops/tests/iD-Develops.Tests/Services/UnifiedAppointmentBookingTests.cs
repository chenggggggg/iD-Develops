using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;

namespace iD_Develops.Tests.Services;

public sealed class UnifiedAppointmentBookingTests
{
    [Fact]
    public async Task AdminBookingChargesAttendeeAndKeepsTheActingUser()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.BookAppointmentAsync(fixture.AdminActor, fixture.Input(fixture.Student.Id));
        Assert.True(result.Success, result.ErrorMessage);
        var booking = await fixture.Db.EventBookings.SingleAsync();
        Assert.Equal(fixture.Student.Id, booking.UserId);
        Assert.Equal(fixture.Admin.Id, booking.BookedByUserId);
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.Id));
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Admin.Id));
        Assert.Equal("Confirmed", (await fixture.Db.AppointmentBookingRequests.SingleAsync()).Status);
        Assert.False((await fixture.Db.ScheduledEvents.SingleAsync()).IsVisibleForStudentBooking);
    }

    [Fact]
    public async Task TeacherCanBookOwnHostForStudentAndAnotherHostForThemselves()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Service.BookAppointmentAsync(fixture.TeacherActor, fixture.Input(fixture.Student.Id))).Success);
        var input = fixture.Input(fixture.Teacher.Id) with { TeacherUserId = fixture.OtherTeacher.Id, StartUtc = fixture.StartUtc.AddHours(2) };
        var result = await fixture.Service.BookAppointmentAsync(fixture.TeacherActor, input);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Teacher.Id));
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.Id));
    }

    [Fact]
    public async Task TeacherCannotBookAnotherHostOnBehalfOfStudent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = fixture.Input(fixture.Student.Id) with { TeacherUserId = fixture.OtherTeacher.Id };
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.TeacherActor, input)).Success);
        Assert.Empty(await fixture.Db.AppointmentBookingRequests.ToListAsync());
        Assert.Equal(0, fixture.Integration.ProvisioningCalls);
    }

    [Fact]
    public async Task StudentCannotForgeAttendeeOrReadTheirBalances()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.False((await fixture.Service.GetBookingDataAsync(fixture.StudentActor, fixture.Admin.Id)).CanAccess);
        Assert.Empty(await fixture.Service.GetSlotsAsync(fixture.StudentActor, fixture.Type.Id, fixture.Teacher.Id, fixture.Date, 1, attendeeUserId: fixture.Admin.Id));
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, fixture.Input(fixture.Admin.Id))).Success);
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Admin.Id));
        Assert.Equal(0, fixture.Integration.ProvisioningCalls);
    }

    [Fact]
    public async Task ReplayedRequestDoesNotProvisionOrSpendAgainAndChangedPayloadIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = fixture.Input(fixture.Student.Id);
        Assert.True((await fixture.Service.BookAppointmentAsync(fixture.AdminActor, input)).Success);
        Assert.True((await fixture.Service.BookAppointmentAsync(fixture.AdminActor, input)).Success);
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.AdminActor, input with { StartUtc = input.StartUtc.AddHours(1) })).Success);
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.TeacherActor, input)).Success);
        Assert.Equal(1, fixture.Integration.ProvisioningCalls);
        Assert.Single(await fixture.Db.EventBookings.ToListAsync());
        Assert.Single(await fixture.Db.AppointmentBookingRequests.ToListAsync());
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.Id));
    }

    [Fact]
    public async Task DefinitiveProviderFailureReturnsCreditsAndAllowsAFreshAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Integration.ProvisioningSucceeds = false;
        var input = fixture.Input(fixture.Student.Id);
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, input)).Success);
        Assert.Equal("Failed", (await fixture.Db.AppointmentBookingRequests.SingleAsync()).Status);
        Assert.Equal(ScheduleEventStatus.Cancelled, (await fixture.Db.ScheduledEvents.SingleAsync()).Status);
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Student.Id));
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, input)).Success);
        Assert.Equal(1, fixture.Integration.ProvisioningCalls);
        fixture.Integration.ProvisioningSucceeds = true;
        Assert.True((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, input with { RequestId = Guid.NewGuid() })).Success);
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainProviderOutcomeKeepsTheTimeAndCreditsHeldWithoutRetrying(bool throws)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Integration.ProvisioningSucceeds = false;
        fixture.Integration.ProvisioningOutcomeUncertain = true;
        if (throws) fixture.Integration.ProvisioningException = new HttpRequestException("Response lost");
        var input = fixture.Input(fixture.Student.Id);
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, input)).Success);
        Assert.Equal("ReviewRequired", (await fixture.Db.AppointmentBookingRequests.SingleAsync()).Status);
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.Id));
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, input)).Success);
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, input with { RequestId = Guid.NewGuid() })).Success);
        Assert.Equal(1, fixture.Integration.ProvisioningCalls);
        var meeting = await fixture.Db.ScheduledEvents.SingleAsync();
        Assert.False((await fixture.Service.CancelAppointmentAsync(fixture.StudentActor, meeting.Id)).Success);
        Assert.False((await fixture.Service.CancelAppointmentAsync(fixture.AdminActor, meeting.Id)).Success);
        Assert.Equal(0, fixture.Integration.CancellationCalls);
    }

    [Fact]
    public async Task InsufficientCreditsFailBeforeMeetingProvisioningOrRequestCreation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var lot = await fixture.Db.UserCreditLots.SingleAsync(item => item.UserId == fixture.Student.Id);
        lot.RemainingQuantity = 1;
        await fixture.Db.SaveChangesAsync();
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, fixture.Input(fixture.Student.Id))).Success);
        Assert.Equal(0, fixture.Integration.ProvisioningCalls);
        Assert.Empty(await fixture.Db.ScheduledEvents.ToListAsync());
        Assert.Empty(await fixture.Db.AppointmentBookingRequests.ToListAsync());
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnCancellationAppliesTheSavedEarlyOrLatePolicyAndReleasesTheSlot(bool late)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Policy.CancellationWindowHours = late ? 1000 : 1;
        fixture.Policy.EarlyCancellationAction = CreditResolutionAction.Return;
        fixture.Policy.LateCancellationAction = CreditResolutionAction.Consume;
        await fixture.Db.SaveChangesAsync();
        var input = fixture.Input(fixture.Student.Id);
        Assert.True((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, input)).Success);
        var meeting = await fixture.Db.ScheduledEvents.SingleAsync();
        var data = await fixture.Service.GetBookingDataAsync(fixture.StudentActor);
        Assert.Contains(late ? "consumed" : "returned", Assert.Single(data.Bookings).CancellationMessage);
        Assert.True((await fixture.Service.CancelAppointmentAsync(fixture.StudentActor, meeting.Id)).Success);
        Assert.Equal(late ? 1 : 3, await fixture.BalanceAsync(fixture.Student.Id));
        Assert.Equal(ScheduleEventStatus.Cancelled, (await fixture.Db.ScheduledEvents.SingleAsync()).Status);
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, input)).Success);
        Assert.Contains(await fixture.Service.GetSlotsAsync(fixture.StudentActor, fixture.Type.Id, fixture.Teacher.Id, fixture.Date, 1), item => item.StartUtc == fixture.StartUtc && item.IsAvailable);
    }

    [Fact]
    public async Task StaffCancellationReturnsOnlyTheAttendeesCreditsOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Service.BookAppointmentAsync(fixture.AdminActor, fixture.Input(fixture.Student.Id))).Success);
        var meeting = await fixture.Db.ScheduledEvents.SingleAsync();
        Assert.False((await fixture.Service.CancelAppointmentAsync(new(fixture.OtherTeacher.Id, false, true), meeting.Id)).Success);
        Assert.True((await fixture.Service.CancelAppointmentAsync(fixture.TeacherActor, meeting.Id)).Success);
        Assert.True((await fixture.Service.CancelAppointmentAsync(fixture.AdminActor, meeting.Id)).Success);
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Student.Id));
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Admin.Id));
        Assert.Single(await fixture.Db.UserCreditTransactions.Where(item => item.TransactionType == CreditTransactionType.Return).ToListAsync());
    }

    [Fact]
    public async Task AttendeeConflictsAndProviderReadinessDisableSlotsBeforeSubmission()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Integration.ConnectionsReady = false;
        var unavailable = await fixture.Service.GetSlotsAsync(fixture.StudentActor, fixture.Type.Id, fixture.Teacher.Id, fixture.Date, 1);
        Assert.All(unavailable, item => Assert.False(item.IsAvailable));
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, fixture.Input(fixture.Student.Id))).Success);
        fixture.Integration.ConnectionsReady = true;
        Assert.True((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, fixture.Input(fixture.Student.Id))).Success);
        var otherHost = await fixture.Service.GetSlotsAsync(fixture.StudentActor, fixture.Type.Id, fixture.OtherTeacher.Id, fixture.Date, 1);
        Assert.Contains(otherHost, item => item.StartUtc == fixture.StartUtc && !item.IsAvailable && item.UnavailableReason!.Contains("attendee"));
    }

    [Fact]
    public async Task PrivateMeetingLinksAreNotExposedInPublicBookingOptions()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Type.DeliveryType = ScheduleDeliveryType.OtherOnline;
        fixture.Type.MeetingUrl = "https://meeting.example.test/private-secret";
        await fixture.Db.SaveChangesAsync();
        var data = await fixture.Service.GetBookingDataAsync(fixture.StudentActor);
        Assert.Null(Assert.Single(data.Options).MeetingUrl);
        var management = await fixture.Service.GetManagementDataAsync(fixture.AdminActor);
        Assert.Equal(fixture.Type.MeetingUrl, Assert.Single(management.AppointmentTypes).MeetingUrl);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public async Task SlotsRespectAbsoluteSessionDurationAcrossAmsterdamClockChanges(int month)
    {
        await using var fixture = await Fixture.CreateAsync();
        var year = DateTime.UtcNow.Year;
        DateOnly date;
        do
        {
            date = new DateOnly(year++, month, DateTime.DaysInMonth(year - 1, month));
            while (date.DayOfWeek != DayOfWeek.Sunday) date = date.AddDays(-1);
        } while (date <= DateOnly.FromDateTime(DateTime.UtcNow));
        await fixture.Db.TeacherAvailabilityWindows.ExecuteDeleteAsync();
        fixture.Db.TeacherAvailabilityWindows.Add(new() { TeacherUserId = fixture.Teacher.Id, DayOfWeek = date.DayOfWeek, LocalStartTime = new(1, 0), LocalEndTime = new(month == 3 ? 3 : 4, 0) });
        await fixture.Db.SaveChangesAsync();
        var slots = await fixture.Service.GetSlotsAsync(fixture.StudentActor, fixture.Type.Id, fixture.Teacher.Id, date, 1);
        if (month == 3)
        {
            Assert.Single(slots);
            Assert.All(slots, slot => Assert.True(slot.EndUtc <= Utc(date, new(3, 0))));
        }
        else
        {
            Assert.Equal(7, slots.Count);
            Assert.Equal(2, slots.Count(slot => TimeZoneInfo.ConvertTimeFromUtc(slot.StartUtc, Zone()).TimeOfDay == new TimeSpan(2, 0, 0)));
            Assert.Equal(slots.Count, slots.Select(item => item.StartUtc).Distinct().Count());
        }
    }

    [Fact]
    public async Task AdministratorCanReleaseUnconfirmedSetupOnlyAfterExplicitProviderReviewAndAuditItOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Integration.ProvisioningSucceeds = false;
        fixture.Integration.ProvisioningOutcomeUncertain = true;
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, fixture.Input(fixture.Student.Id))).Success);
        var request = await fixture.Db.AppointmentBookingRequests.SingleAsync();
        Assert.False((await fixture.Service.ResolveSetupAsync(fixture.StudentActor, request.Id, true)).Success);
        Assert.False((await fixture.Service.ResolveSetupAsync(fixture.TeacherActor, request.Id, true)).Success);
        Assert.False((await fixture.Service.ResolveSetupAsync(fixture.AdminActor, request.Id, false)).Success);
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.Id));
        var issues = (await fixture.Service.GetBookingDataAsync(fixture.AdminActor)).SetupIssues!;
        Assert.Equal(request.Id, Assert.Single(issues).RequestId);
        Assert.True((await fixture.Service.ResolveSetupAsync(fixture.AdminActor, request.Id, true)).Success);
        Assert.True((await fixture.Service.ResolveSetupAsync(fixture.AdminActor, request.Id, true)).Success);
        Assert.Equal(3, await fixture.BalanceAsync(fixture.Student.Id));
        var audit = await fixture.Db.ApplicationAuditEvents.SingleAsync(item => item.EventType == "scheduling.appointment.setup_resolved");
        Assert.Equal(fixture.Admin.Id, audit.ActorUserId);
        Assert.Equal(fixture.Student.Id, audit.SubjectUserId);
        Assert.Empty((await fixture.Service.GetBookingDataAsync(fixture.AdminActor)).SetupIssues!);
    }

    [Fact]
    public async Task UnconfirmedSetupCannotBeJoinedOrChargedAsANoShow()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Integration.ProvisioningSucceeds = false;
        fixture.Integration.ProvisioningOutcomeUncertain = true;
        Assert.False((await fixture.Service.BookAppointmentAsync(fixture.StudentActor, fixture.Input(fixture.Student.Id))).Success);
        var meeting = await fixture.Db.ScheduledEvents.SingleAsync();
        meeting.DeliveryType = ScheduleDeliveryType.Zoom;
        meeting.ZoomMeetingId = "unconfirmed-meeting";
        meeting.MeetingUrl = "https://zoom.example.test/unconfirmed";
        meeting.StartAtUtc = DateTime.UtcNow.AddHours(-2);
        meeting.EndAtUtc = DateTime.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["SchedulingProviders:Zoom:Enabled"] = "true" }).Build();
        var attendance = new ZoomAttendanceService(fixture.Db, fixture.Integration, configuration, NullLogger<ZoomAttendanceService>.Instance);
        Assert.Equal(0, await attendance.ReconcileDueMeetingsAsync());
        Assert.False((await attendance.OverrideAttendanceAsync(fixture.AdminActor, (await fixture.Db.EventBookings.SingleAsync()).Id, EventBookingStatus.NoShow)).Success);
        var calendar = new SchedulingService(fixture.Db, fixture.Integration, NullLogger<SchedulingService>.Instance);
        var join = await calendar.GetJoinUrlAsync(fixture.StudentActor, meeting.Id);
        Assert.False(join.Success);
        Assert.Contains("confirmation", join.ErrorMessage);
        var item = Assert.Single(await calendar.GetCalendarItemsAsync(fixture.StudentActor, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(1)));
        Assert.False(item.CanJoin);
        Assert.False(item.CanCancelBooking);
        Assert.Equal(1, await fixture.BalanceAsync(fixture.Student.Id));
        Assert.Equal("ReviewRequired", (await fixture.Db.AppointmentBookingRequests.SingleAsync()).Status);
    }

    private static TimeZoneInfo Zone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
    }
    private static DateTime Utc(DateOnly date, TimeOnly time) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), Zone());

    private sealed class Fixture(SqliteTestDbFactory factory, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public ApplicationUser Student { get; private set; } = null!;
        public ApplicationUser Admin { get; private set; } = null!;
        public ApplicationUser Teacher { get; private set; } = null!;
        public ApplicationUser OtherTeacher { get; private set; } = null!;
        public AppointmentType Type { get; private set; } = null!;
        public CreditConsumptionPolicy Policy { get; private set; } = null!;
        public DateOnly Date { get; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        public DateTime StartUtc => Utc(Date, new(9, 0));
        public FakeSchedulingIntegrationService Integration { get; } = new();
        public AppointmentService Service { get; private set; } = null!;
        public ScheduleActor AdminActor => new(Admin.Id, true, false);
        public ScheduleActor TeacherActor => new(Teacher.Id, false, true);
        public ScheduleActor StudentActor => new(Student.Id, false, false);
        public AppointmentBookingInput Input(string attendee) => new(Guid.NewGuid(), Type.Id, Teacher.Id, StartUtc, attendee);
        public Task<int> BalanceAsync(string id) => Db.UserCreditLots.Where(item => item.UserId == id).SumAsync(item => item.RemainingQuantity);

        public static async Task<Fixture> CreateAsync()
        {
            var factory = new SqliteTestDbFactory();
            var fixture = new Fixture(factory, factory.CreateDbContext());
            fixture.Student = User("unified-student"); fixture.Teacher = User("unified-teacher");
            fixture.OtherTeacher = User("unified-other"); fixture.Admin = User("unified-admin");
            fixture.Db.Users.AddRange(fixture.Student, fixture.Teacher, fixture.OtherTeacher, fixture.Admin);
            foreach (var name in new[] { "Student", "Teacher", "Admin" }) fixture.Db.Roles.Add(new(name) { Id = name, NormalizedName = name.ToUpperInvariant() });
            fixture.Db.UserRoles.AddRange(new IdentityUserRole<string> { UserId = fixture.Student.Id, RoleId = "Student" },
                new() { UserId = fixture.Teacher.Id, RoleId = "Teacher" }, new() { UserId = fixture.OtherTeacher.Id, RoleId = "Teacher" }, new() { UserId = fixture.Admin.Id, RoleId = "Admin" });
            var credit = new CreditType { Name = "Session credit", NormalizedName = "SESSION CREDIT", SingularLabel = "session credit", PluralLabel = "session credits" };
            fixture.Policy = new() { Name = "Session policy", NormalizedName = "SESSION POLICY", CancellationWindowHours = 1, EarlyCancellationAction = CreditResolutionAction.Return, StaffCancellationAction = CreditResolutionAction.Return };
            fixture.Db.CreditTypes.Add(credit); fixture.Db.CreditConsumptionPolicies.Add(fixture.Policy);
            await fixture.Db.SaveChangesAsync();
            fixture.Type = new()
            {
                Name = "Coaching",
                CreatedByUserId = fixture.Admin.Id,
                DurationMinutes = 60,
                DeliveryType = ScheduleDeliveryType.InPerson,
                Location = "Studio",
                RequiredCreditTypeId = credit.Id,
                CreditCost = 2,
                CreditConsumptionPolicyId = fixture.Policy.Id,
                Teachers = [new() { TeacherUserId = fixture.Teacher.Id }, new() { TeacherUserId = fixture.OtherTeacher.Id }]
            };
            fixture.Db.AppointmentTypes.Add(fixture.Type);
            foreach (var host in new[] { fixture.Teacher, fixture.OtherTeacher }) fixture.Db.TeacherAvailabilityWindows.Add(new() { TeacherUserId = host.Id, DayOfWeek = fixture.Date.DayOfWeek, LocalStartTime = new(8, 0), LocalEndTime = new(18, 0) });
            foreach (var user in new[] { fixture.Student, fixture.Admin, fixture.Teacher }) fixture.Db.UserCreditLots.Add(new() { UserId = user.Id, CreditTypeId = credit.Id, GrantedQuantity = 3, RemainingQuantity = 3, Scope = CreditGrantScope.Global });
            await fixture.Db.SaveChangesAsync();
            var scheduling = new SchedulingService(fixture.Db, fixture.Integration, NullLogger<SchedulingService>.Instance);
            fixture.Service = new(fixture.Db, scheduling, fixture.Integration, NullLogger<AppointmentService>.Instance);
            return fixture;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); factory.Dispose(); }
        private static ApplicationUser User(string id) => new() { Id = id, UserName = id + "@example.test", Email = id + "@example.test", FirstName = id, LastName = "Fixture", EmailConfirmed = true };
    }
}
