using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record SchedulingProviderStatus(
        SchedulingProvider Provider,
        string DisplayName,
        bool IsConfigured,
        bool IsConnected,
        string? AccountEmail,
        DateTime? ConnectedAtUtc,
        DateTime? LastSuccessfulSyncAtUtc,
        string? LastError);

    public sealed record SchedulingAuthorizationResult(bool Success, string? AuthorizationUrl, string? ErrorMessage);

    public sealed record ExternalAvailabilityResult(bool Success, bool IsAvailable, string? ErrorMessage);

    public sealed record ExternalBusyPeriod(DateTime StartUtc, DateTime EndUtc);

    public sealed record ExternalBusyPeriodsResult(
        bool Success,
        IReadOnlyList<ExternalBusyPeriod> Periods,
        string? ErrorMessage);

    public sealed record SchedulingJoinResult(bool Success, string? JoinUrl, string? ErrorMessage);

    public sealed record GoogleCalendarSyncResult(
        bool Success,
        int CreatedCount,
        int UpdatedCount,
        int FailedCount,
        string? ErrorMessage);

    public sealed record ZoomAttendanceParticipant(
        string RegistrantId,
        DateTime? JoinedAtUtc,
        DateTime? LeftAtUtc,
        int DurationSeconds);

    public sealed record ZoomAttendanceReportResult(
        bool Success,
        IReadOnlyList<ZoomAttendanceParticipant> Participants,
        string? ErrorMessage);

    public interface ISchedulingIntegrationService
    {
        Task<IReadOnlyList<SchedulingProviderStatus>> GetStatusesAsync(string userId, CancellationToken cancellationToken = default);
        SchedulingAuthorizationResult CreateAuthorizationUrl(SchedulingProvider provider, string userId, string redirectUri);
        Task<OperationResult> CompleteAuthorizationAsync(SchedulingProvider provider, string userId, string code, string state, string redirectUri, CancellationToken cancellationToken = default);
        Task<OperationResult> DisconnectAsync(SchedulingProvider provider, string userId, CancellationToken cancellationToken = default);
        Task<GoogleCalendarSyncResult> SyncGoogleCalendarAsync(string teacherUserId, CancellationToken cancellationToken = default);
        Task<OperationResult> SyncEventAttendeesAsync(int scheduledEventId, CancellationToken cancellationToken = default);
        Task<OperationResult> RegisterBookingAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default);
        Task<OperationResult> CancelBookingRegistrationAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default);
        Task<SchedulingJoinResult> GetBookingJoinUrlAsync(int scheduledEventId, string userId, CancellationToken cancellationToken = default);
        Task<ZoomAttendanceReportResult> GetZoomAttendanceReportAsync(int scheduledEventId, CancellationToken cancellationToken = default);
        Task<OperationResult> ValidateTeacherConnectionsAsync(string teacherUserId, ScheduleDeliveryType deliveryType, bool requiresGeneratedZoom, CancellationToken cancellationToken = default);
        Task<ExternalAvailabilityResult> CheckAvailabilityAsync(string teacherUserId, DateTime startUtc, DateTime endUtc, DateTime? excludedStartUtc = null, DateTime? excludedEndUtc = null, CancellationToken cancellationToken = default);
        Task<ExternalBusyPeriodsResult> GetBusyPeriodsAsync(string teacherUserId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
        Task<OperationResult> ProvisionEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken = default);
        Task<OperationResult> UpdateEventAsync(ScheduledEvent scheduleEvent, DateTime newStartUtc, DateTime newEndUtc, CancellationToken cancellationToken = default);
        Task<OperationResult> CancelEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken = default);
    }
}
