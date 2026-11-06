using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record AppointmentTeacherItem(string Id, string Name, string Email);

    public sealed record AppointmentTypeItem(
        int Id,
        string Name,
        int DurationMinutes,
        int? RequiredCreditTypeId,
        string CreditName,
        string CreditLabel,
        int CreditCost,
        int? CreditConsumptionPolicyId,
        string CreditPolicyName,
        bool IsActive,
        bool CanEdit,
        IReadOnlyList<AppointmentTeacherItem> Teachers,
        int AvailableCreditQuantity = 0,
        Enums.ScheduleDeliveryType DeliveryType = Enums.ScheduleDeliveryType.Zoom,
        string? MeetingUrl = null,
        string? Location = null)
    {
        public bool RequiresCredits => RequiredCreditTypeId.HasValue;
    }

    public sealed record AppointmentManagementData(
        IReadOnlyList<AppointmentTypeItem> AppointmentTypes,
        IReadOnlyList<AppointmentTeacherItem> Teachers,
        IReadOnlyList<CreditOption> CreditTypes,
        IReadOnlyList<CreditPolicyOption> CreditPolicies);

    public sealed record CreditOption(int Id, string Name, string SingularLabel, string PluralLabel);
    public sealed record CreditPolicyOption(int Id, string Name);

    public sealed class AppointmentTypeInput
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DurationMinutes { get; set; } = 60;
        public int? RequiredCreditTypeId { get; set; }
        public int CreditCost { get; set; } = 1;
        public int? CreditConsumptionPolicyId { get; set; }
        public IReadOnlyList<string> TeacherUserIds { get; set; } = Array.Empty<string>();
        public bool IsActive { get; set; } = true;
        public Enums.ScheduleDeliveryType DeliveryType { get; set; } = Enums.ScheduleDeliveryType.Zoom;
        public string? MeetingUrl { get; set; }
        public string? Location { get; set; }
    }

    public sealed record AppointmentSlot(
        DateTime StartUtc,
        DateTime EndUtc,
        bool IsAvailable,
        string? UnavailableReason);

    public sealed record AppointmentAttendee(string Id, string Name, string Email);
    public sealed record AppointmentBookingItem(int EventId, string Name, string TeacherName, DateTime StartUtc,
        DateTime EndUtc, string Status, string? SetupStatus, string? SetupMessage, bool CanCancel,
        string CancellationMessage = "", Guid? RequestId = null);
    public sealed record AppointmentBookingData(bool CanAccess, string AttendeeUserId, string AttendeeName,
        IReadOnlyList<AppointmentTypeItem> Options, IReadOnlyList<AppointmentAttendee> Attendees,
        IReadOnlyList<AppointmentBookingItem> Bookings, IReadOnlyList<AppointmentSetupIssue>? SetupIssues = null);
    public sealed record AppointmentSetupIssue(Guid RequestId, int EventId, string Name, string AttendeeName, DateTime StartUtc, string Status, string? Message, string TeacherName = "");
    public sealed record AppointmentBookingInput(Guid RequestId, int AppointmentTypeId, string TeacherUserId,
        DateTime StartUtc, string? AttendeeUserId = null);

    public interface IAppointmentService
    {
        Task<AppointmentManagementData> GetManagementDataAsync(ScheduleActor actor, CancellationToken cancellationToken = default);
        Task<OperationResult> SaveAppointmentTypeAsync(ScheduleActor actor, AppointmentTypeInput input, CancellationToken cancellationToken = default);
        Task<OperationResult> DeactivateAppointmentTypeAsync(ScheduleActor actor, int appointmentTypeId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AppointmentTypeItem>> GetStudentOptionsAsync(ScheduleActor actor, CancellationToken cancellationToken = default);
        Task<AppointmentBookingData> GetBookingDataAsync(ScheduleActor actor, string? attendeeUserId = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AppointmentSlot>> GetSlotsAsync(ScheduleActor actor, int appointmentTypeId, string teacherUserId, DateOnly startDate, int days, CancellationToken cancellationToken = default, string? attendeeUserId = null);
        Task<OperationResult> BookAppointmentAsync(ScheduleActor actor, int appointmentTypeId, string teacherUserId, DateTime startUtc, CancellationToken cancellationToken = default);
        Task<OperationResult> BookAppointmentAsync(ScheduleActor actor, AppointmentBookingInput input, CancellationToken cancellationToken = default);
        Task<OperationResult> CancelAppointmentAsync(ScheduleActor actor, int scheduledEventId, CancellationToken cancellationToken = default);
        Task<OperationResult> ResolveSetupAsync(ScheduleActor actor, Guid requestId, bool providerCleanupConfirmed, CancellationToken cancellationToken = default);
    }
}
