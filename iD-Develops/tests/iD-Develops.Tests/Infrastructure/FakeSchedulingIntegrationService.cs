using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;

namespace iD_Develops.Tests.Infrastructure;

internal sealed class FakeSchedulingIntegrationService : ISchedulingIntegrationService
{
    private int _sequence;
    public TeacherZoomReadiness ZoomReadiness { get; set; } = new(ZoomReadinessState.Ready, "Test teacher is connected.");
    public bool ProvisioningSucceeds { get; set; } = true;
    public bool ProvisioningOutcomeUncertain { get; set; }
    public bool ConnectionsReady { get; set; } = true;
    public int CancellationCalls { get; private set; }
    public bool ReturnMeetingDetails { get; set; } = true;
    public Exception? ProvisioningException { get; set; }
    public int ProvisioningCalls { get; private set; }
    public Task<TeacherZoomReadiness> GetZoomReadinessAsync(string teacherUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(ZoomReadiness);


    public Task<IReadOnlyList<SchedulingProviderStatus>> GetStatusesAsync(string userId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SchedulingProviderStatus>>(Array.Empty<SchedulingProviderStatus>());

    public SchedulingAuthorizationResult CreateAuthorizationUrl(SchedulingProvider provider, string userId, string redirectUri)
        => new(false, null, "Not available in tests.");

    public Task<OperationResult> CompleteAuthorizationAsync(SchedulingProvider provider, string userId, string code, string state, string redirectUri, CancellationToken cancellationToken = default)
        => Task.FromResult(Success());

    public Task<OperationResult> DisconnectAsync(SchedulingProvider provider, string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Success());

    public Task<GoogleCalendarSyncResult> SyncGoogleCalendarAsync(string teacherUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(new GoogleCalendarSyncResult(true, 0, 0, 0, null));

    public Task<OperationResult> SyncEventAttendeesAsync(int scheduledEventId, CancellationToken cancellationToken = default)
        => Task.FromResult(Success());

    public Task<OperationResult> RegisterBookingAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Success());

    public Task<OperationResult> CancelBookingRegistrationAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Success());

    public Task<SchedulingJoinResult> GetBookingJoinUrlAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(new SchedulingJoinResult(true, $"https://zoom.example.test/join/{scheduledEventId}/{userId}", null));

    public Task<ZoomAttendanceReportResult> GetZoomAttendanceReportAsync(int scheduledEventId, CancellationToken cancellationToken = default)
        => Task.FromResult(new ZoomAttendanceReportResult(true, Array.Empty<ZoomAttendanceParticipant>(), null));

    public Task<OperationResult> ValidateTeacherConnectionsAsync(string teacherUserId, ScheduleDeliveryType deliveryType, bool requiresGeneratedZoom, CancellationToken cancellationToken = default)
        => Task.FromResult(ConnectionsReady ? Success() : new OperationResult { Success = false, ErrorMessage = "Teacher connection unavailable." });

    public Task<ExternalAvailabilityResult> CheckAvailabilityAsync(string teacherUserId, DateTime startUtc, DateTime endUtc, DateTime? excludedStartUtc = null, DateTime? excludedEndUtc = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ExternalAvailabilityResult(true, true, null));

    public Task<ExternalBusyPeriodsResult> GetBusyPeriodsAsync(string teacherUserId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(new ExternalBusyPeriodsResult(true, Array.Empty<ExternalBusyPeriod>(), null));

    public Task<OperationResult> ProvisionEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken = default)
    {
        ProvisioningCalls++;
        if (ProvisioningException != null) throw ProvisioningException;
        if (!ProvisioningSucceeds) return Task.FromResult(new OperationResult { Success = false, ErrorMessage = "Test provider unavailable.", OutcomeUncertain = ProvisioningOutcomeUncertain });
        var sequence = Interlocked.Increment(ref _sequence);
        if (ReturnMeetingDetails && scheduleEvent.DeliveryType == ScheduleDeliveryType.Zoom && string.IsNullOrWhiteSpace(scheduleEvent.MeetingUrl))
        {
            scheduleEvent.ZoomMeetingId = $"zoom-{sequence}";
            scheduleEvent.MeetingUrl = $"https://zoom.example.test/j/{sequence}";
        }
        scheduleEvent.GoogleCalendarEventId = $"google-{sequence}";
        scheduleEvent.GoogleCalendarId = "primary";
        scheduleEvent.ExternalSyncedAtUtc = DateTime.UtcNow;
        return Task.FromResult(Success());
    }

    public Task<OperationResult> UpdateEventAsync(ScheduledEvent scheduleEvent, DateTime newStartUtc, DateTime newEndUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(Success());

    public Task<OperationResult> CancelEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken = default)
    {
        CancellationCalls++;
        return Task.FromResult(Success());
    }

    private static OperationResult Success() => new() { Success = true, ErrorMessage = string.Empty };
}
