using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace iD_Develops.Tests.Services;

public sealed class ZoomAttendanceServiceTests
{
    private const string WebhookSecret = "zoom-webhook-secret";

    [Fact]
    public async Task ReconcileDueMeetingsAsync_SettlesAttendedAndNoShowCredits()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var teacher = User("teacher", "teacher@example.com");
        var attendee = User("attendee", "attendee@example.com");
        var absent = User("absent", "absent@example.com");
        var creditType = new CreditType
        {
            Name = "Lesson",
            NormalizedName = "LESSON",
            SingularLabel = "lesson",
            PluralLabel = "lessons"
        };
        var policy = new CreditConsumptionPolicy
        {
            Name = "Automated attendance",
            NormalizedName = "AUTOMATED ATTENDANCE",
            AttendedAction = CreditResolutionAction.Consume,
            NoShowAction = CreditResolutionAction.Return
        };
        db.Users.AddRange(teacher, attendee, absent);
        db.CreditTypes.Add(creditType);
        db.CreditConsumptionPolicies.Add(policy);
        await db.SaveChangesAsync();
        var attendeeLot = Lot(attendee.Id, creditType.Id);
        var absentLot = Lot(absent.Id, creditType.Id);
        db.UserCreditLots.AddRange(attendeeLot, absentLot);
        var now = DateTime.UtcNow;
        var scheduleEvent = new ScheduledEvent
        {
            TeacherUserId = teacher.Id,
            Title = "Speaking class",
            CourseNameSnapshot = "Speaking",
            ClassNameSnapshot = "Speaking class",
            TeacherNameSnapshot = "Teacher",
            StartAtUtc = now.AddHours(-1),
            EndAtUtc = now.AddMinutes(-20),
            BookingOpensAtUtc = now.AddDays(-7),
            BookingClosesAtUtc = now.AddHours(-2),
            DeliveryType = ScheduleDeliveryType.Zoom,
            ZoomMeetingId = "123456789",
            ZoomEndedAtUtc = now.AddMinutes(-20),
            CreditConsumptionPolicyId = policy.Id,
            CreditConsumptionPolicy = policy
        };
        var attendedBooking = Booking(scheduleEvent, attendee, "registrant-attended", attendeeLot);
        var absentBooking = Booking(scheduleEvent, absent, "registrant-absent", absentLot);
        db.ScheduledEvents.Add(scheduleEvent);
        db.EventBookings.AddRange(attendedBooking, absentBooking);
        await db.SaveChangesAsync();
        var integration = new AttendanceIntegrationStub(new ZoomAttendanceReportResult(
            true,
            [new ZoomAttendanceParticipant("registrant-attended", now.AddMinutes(-55), now.AddMinutes(-20), 2100)],
            null));
        var service = CreateService(db, integration);

        var count = await service.ReconcileDueMeetingsAsync();

        Assert.Equal(1, count);
        Assert.Equal(EventBookingStatus.Attended, attendedBooking.Status);
        Assert.Equal(EventBookingStatus.NoShow, absentBooking.Status);
        Assert.Equal(CreditResolutionAction.Consume, attendedBooking.CreditResolution);
        Assert.Equal(CreditResolutionAction.Return, absentBooking.CreditResolution);
        Assert.True(attendedBooking.CreditAllocations.Single().IsConsumed);
        Assert.True(absentBooking.CreditAllocations.Single().IsReturned);
        Assert.Equal(0, attendeeLot.RemainingQuantity);
        Assert.Equal(1, absentLot.RemainingQuantity);
        Assert.Equal(2, await db.UserCreditTransactions.CountAsync());
        Assert.NotNull(scheduleEvent.AttendanceReconciledAtUtc);

        var actor = new ScheduleActor("admin", true, false);
        var attendeeCorrection = await service.OverrideAttendanceAsync(
            actor,
            attendedBooking.Id,
            EventBookingStatus.NoShow);
        var absentCorrection = await service.OverrideAttendanceAsync(
            actor,
            absentBooking.Id,
            EventBookingStatus.Attended);

        Assert.True(attendeeCorrection.Success, attendeeCorrection.ErrorMessage);
        Assert.True(absentCorrection.Success, absentCorrection.ErrorMessage);
        Assert.Equal(1, attendeeLot.RemainingQuantity);
        Assert.Equal(0, absentLot.RemainingQuantity);
        Assert.True(attendedBooking.CreditAllocations.Single().IsReturned);
        Assert.True(absentBooking.CreditAllocations.Single().IsConsumed);
        Assert.Equal(4, await db.UserCreditTransactions.CountAsync());
    }

    [Fact]
    public async Task HandleWebhookAsync_AggregatesJoinAndLeaveByRegistrant()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var teacher = User("teacher", "teacher@example.com");
        var student = User("student", "student@example.com");
        var now = DateTime.UtcNow;
        var scheduleEvent = new ScheduledEvent
        {
            TeacherUserId = teacher.Id,
            Title = "Speaking class",
            CourseNameSnapshot = "Speaking",
            ClassNameSnapshot = "Speaking class",
            TeacherNameSnapshot = "Teacher",
            StartAtUtc = now.AddMinutes(-45),
            EndAtUtc = now.AddMinutes(15),
            BookingOpensAtUtc = now.AddDays(-7),
            BookingClosesAtUtc = now.AddHours(-1),
            DeliveryType = ScheduleDeliveryType.Zoom,
            ZoomMeetingId = "987654321"
        };
        var booking = new EventBooking
        {
            ScheduledEvent = scheduleEvent,
            User = student,
            UserId = student.Id,
            ZoomRegistrantId = "registrant-1",
            ZoomRegistrationStatus = ZoomRegistrationStatus.Registered
        };
        db.Users.AddRange(teacher, student);
        db.EventBookings.Add(booking);
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new AttendanceIntegrationStub(new ZoomAttendanceReportResult(true, [], null)));
        var joinedAt = now.AddMinutes(-30);
        var leftAt = now;

        var joined = WebhookBody("meeting.participant_joined", scheduleEvent.ZoomMeetingId!, "registrant-1", "session-1", joinedAt, null);
        var joinedResult = await SendWebhookAsync(service, joined);
        var left = WebhookBody("meeting.participant_left", scheduleEvent.ZoomMeetingId!, "registrant-1", "session-1", null, leftAt);
        var leftResult = await SendWebhookAsync(service, left);

        Assert.True(joinedResult.Success, joinedResult.ErrorMessage);
        Assert.True(leftResult.Success, leftResult.ErrorMessage);
        var segment = Assert.Single(booking.ZoomAttendanceSegments);
        Assert.InRange(segment.DurationSeconds, 1799, 1801);
        Assert.Equal(segment.DurationSeconds, booking.ZoomAttendanceSeconds);
        Assert.Equal(joinedAt, booking.FirstJoinedAtUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(leftAt, booking.LastLeftAtUtc!.Value, TimeSpan.FromSeconds(1));
    }

    private static ZoomAttendanceService CreateService(
        iD_Develops.Data.ApplicationDbContext db,
        ISchedulingIntegrationService integration)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SchedulingProviders:Zoom:WebhookSecret"] = WebhookSecret,
                ["SchedulingProviders:Zoom:AttendanceMinimumMinutes"] = "15",
                ["SchedulingProviders:Zoom:AttendanceMinimumPercent"] = "25"
            })
            .Build();
        return new ZoomAttendanceService(
            db,
            integration,
            configuration,
            NullLogger<ZoomAttendanceService>.Instance);
    }

    private static async Task<ZoomWebhookHandlingResult> SendWebhookAsync(
        ZoomAttendanceService service,
        string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        var hash = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"v0:{timestamp}:{body}"))).ToLowerInvariant();
        return await service.HandleWebhookAsync(body, timestamp, $"v0={hash}");
    }

    private static string WebhookBody(
        string eventName,
        string meetingId,
        string registrantId,
        string participantUuid,
        DateTime? joinedAt,
        DateTime? leftAt)
        => JsonSerializer.Serialize(new
        {
            @event = eventName,
            event_ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            payload = new
            {
                @object = new
                {
                    id = long.Parse(meetingId),
                    uuid = "meeting-uuid",
                    participant = new
                    {
                        registrant_id = registrantId,
                        participant_uuid = participantUuid,
                        join_time = joinedAt?.ToString("O"),
                        leave_time = leftAt?.ToString("O")
                    }
                }
            }
        });

    private static EventBooking Booking(
        ScheduledEvent scheduleEvent,
        ApplicationUser user,
        string registrantId,
        UserCreditLot lot)
    {
        var booking = new EventBooking
        {
            ScheduledEvent = scheduleEvent,
            User = user,
            UserId = user.Id,
            ZoomRegistrantId = registrantId,
            ZoomRegistrationStatus = ZoomRegistrationStatus.Registered
        };
        booking.CreditAllocations.Add(new EventBookingCreditAllocation
        {
            UserCreditLot = lot,
            Amount = 1
        });
        return booking;
    }

    private static UserCreditLot Lot(string userId, int creditTypeId)
        => new()
        {
            UserId = userId,
            CreditTypeId = creditTypeId,
            GrantedQuantity = 1,
            RemainingQuantity = 0,
            Scope = CreditGrantScope.Global,
            GrantedAtUtc = DateTime.UtcNow
        };

    private static ApplicationUser User(string id, string email)
        => new()
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = id,
            LastName = "User",
            EmailConfirmed = true
        };

    private sealed class AttendanceIntegrationStub(ZoomAttendanceReportResult report) : ISchedulingIntegrationService
    {
        public Task<ZoomAttendanceReportResult> GetZoomAttendanceReportAsync(int scheduledEventId, CancellationToken cancellationToken = default)
            => Task.FromResult(report);
        public Task<OperationResult> RegisterBookingAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        public Task<OperationResult> CancelBookingRegistrationAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        public Task<SchedulingJoinResult> GetBookingJoinUrlAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new SchedulingJoinResult(false, null, "Not used."));
        public Task<IReadOnlyList<SchedulingProviderStatus>> GetStatusesAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SchedulingProviderStatus>>([]);
        public SchedulingAuthorizationResult CreateAuthorizationUrl(SchedulingProvider provider, string userId, string redirectUri)
            => new(false, null, "Not used.");
        public Task<OperationResult> CompleteAuthorizationAsync(SchedulingProvider provider, string userId, string code, string state, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        public Task<OperationResult> DisconnectAsync(SchedulingProvider provider, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        public Task<GoogleCalendarSyncResult> SyncGoogleCalendarAsync(string teacherUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(new GoogleCalendarSyncResult(true, 0, 0, 0, null));
        public Task<OperationResult> SyncEventAttendeesAsync(int scheduledEventId, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        public Task<OperationResult> ValidateTeacherConnectionsAsync(string teacherUserId, ScheduleDeliveryType deliveryType, bool requiresGeneratedZoom, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        public Task<ExternalAvailabilityResult> CheckAvailabilityAsync(string teacherUserId, DateTime startUtc, DateTime endUtc, DateTime? excludedStartUtc = null, DateTime? excludedEndUtc = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExternalAvailabilityResult(true, true, null));
        public Task<ExternalBusyPeriodsResult> GetBusyPeriodsAsync(string teacherUserId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExternalBusyPeriodsResult(true, Array.Empty<ExternalBusyPeriod>(), null));
        public Task<OperationResult> ProvisionEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        public Task<OperationResult> UpdateEventAsync(ScheduledEvent scheduleEvent, DateTime newStartUtc, DateTime newEndUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        public Task<OperationResult> CancelEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken = default)
            => Task.FromResult(Success());
        private static OperationResult Success() => new() { Success = true, ErrorMessage = string.Empty };
    }
}
