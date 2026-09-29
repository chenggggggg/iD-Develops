using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record AppointmentTeacherItem(string Id, string Name, string Email);

    public sealed record AppointmentTypeItem(
        int Id,
        string Name,
        int DurationMinutes,
        int RequiredCreditTypeId,
        string CreditName,
        string CreditLabel,
        int CreditCost,
        int CreditConsumptionPolicyId,
        string CreditPolicyName,
        bool IsActive,
        bool CanEdit,
        IReadOnlyList<AppointmentTeacherItem> Teachers,
        int AvailableCreditQuantity = 0);

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
        public int RequiredCreditTypeId { get; set; }
        public int CreditCost { get; set; } = 1;
        public int CreditConsumptionPolicyId { get; set; }
        public IReadOnlyList<string> TeacherUserIds { get; set; } = Array.Empty<string>();
        public bool IsActive { get; set; } = true;
    }

    public sealed record AppointmentSlot(
        DateTime StartUtc,
        DateTime EndUtc,
        bool IsAvailable,
        string? UnavailableReason);

    public interface IAppointmentService
    {
        Task<AppointmentManagementData> GetManagementDataAsync(ScheduleActor actor, CancellationToken cancellationToken = default);
        Task<OperationResult> SaveAppointmentTypeAsync(ScheduleActor actor, AppointmentTypeInput input, CancellationToken cancellationToken = default);
        Task<OperationResult> DeactivateAppointmentTypeAsync(ScheduleActor actor, int appointmentTypeId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AppointmentTypeItem>> GetStudentOptionsAsync(ScheduleActor actor, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AppointmentSlot>> GetSlotsAsync(ScheduleActor actor, int appointmentTypeId, string teacherUserId, DateOnly startDate, int days, CancellationToken cancellationToken = default);
        Task<OperationResult> BookAppointmentAsync(ScheduleActor actor, int appointmentTypeId, string teacherUserId, DateTime startUtc, CancellationToken cancellationToken = default);
    }
}
