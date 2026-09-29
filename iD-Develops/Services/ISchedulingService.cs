using iD_Develops.Enums;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record ScheduleActor(string UserId, bool IsAdmin, bool IsTeacher);

    public sealed record ScheduleClassOption(
        int Id,
        int CourseId,
        string CourseName,
        string SectionTitle,
        string ClassTitle,
        int DurationMinutes,
        int Capacity,
        CourseClassFormat Format);

    public sealed record ScheduleTeacherOption(string Id, string Name, string Email);

    public sealed record ScheduleStudentOption(string Id, string Email);

    public sealed record TeacherAvailabilityListItem(
        string TeacherUserId,
        DayOfWeek DayOfWeek,
        TimeOnly LocalStartTime,
        TimeOnly LocalEndTime,
        string TimeZoneId);

    public sealed record ScheduleRosterListItem(
        int Id,
        string TeacherUserId,
        string TeacherName,
        int CourseClassId,
        string CourseName,
        string ClassTitle,
        DayOfWeek DayOfWeek,
        TimeOnly LocalStartTime,
        string TimeZoneId,
        DateOnly ActiveFromDate,
        DateOnly? ActiveUntilDate,
        int DurationMinutes,
        int Capacity,
        ScheduleDeliveryType DeliveryType,
        string? MeetingUrl,
        string? Location,
        int BookingOpenDaysBefore,
        int BookingCloseHoursBefore,
        int GenerateWeeksAhead,
        bool IsVisibleForStudentBooking,
        bool IsActive,
        int UpcomingEventCount);

    public sealed record ScheduleRosterPageData(
        IReadOnlyList<ScheduleRosterListItem> Rules,
        IReadOnlyList<ScheduleClassOption> Classes,
        IReadOnlyList<ScheduleTeacherOption> Teachers,
        IReadOnlyList<ScheduleStudentOption> Students,
        IReadOnlyList<TeacherAvailabilityListItem> Availability);

    public sealed class TeacherAvailabilityDayInput
    {
        public DayOfWeek DayOfWeek { get; set; }
        public bool IsAvailable { get; set; }
        public TimeOnly LocalStartTime { get; set; } = new(9, 0);
        public TimeOnly LocalEndTime { get; set; } = new(17, 0);
    }

    public sealed class TeacherAvailabilityInput
    {
        public string? TeacherUserId { get; set; }
        public string TimeZoneId { get; set; } = "Europe/Amsterdam";
        public IReadOnlyList<TeacherAvailabilityDayInput> Days { get; set; } = Array.Empty<TeacherAvailabilityDayInput>();
    }

    public sealed class ScheduleRosterRuleInput
    {
        public int Id { get; set; }
        public string? TeacherUserId { get; set; }
        public int CourseClassId { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public TimeOnly LocalStartTime { get; set; } = new(9, 0);
        public string TimeZoneId { get; set; } = "Europe/Amsterdam";
        public DateOnly ActiveFromDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
        public DateOnly? ActiveUntilDate { get; set; }
        public int DurationMinutes { get; set; } = 60;
        public int Capacity { get; set; } = 1;
        public ScheduleDeliveryType DeliveryType { get; set; } = ScheduleDeliveryType.Zoom;
        public string? MeetingUrl { get; set; }
        public string? Location { get; set; }
        public int BookingOpenDaysBefore { get; set; } = 30;
        public int BookingCloseHoursBefore { get; set; } = 1;
        public int GenerateWeeksAhead { get; set; } = 12;
        public bool IsVisibleForStudentBooking { get; set; } = true;
        public bool IsActive { get; set; } = true;
    }

    public sealed class ManualScheduleEventInput
    {
        public int? CourseClassId { get; set; }
        public string? StudentUserId { get; set; }
        public string? TeacherUserId { get; set; }
        public DateTime StartLocal { get; set; }
        public string TimeZoneId { get; set; } = "Europe/Amsterdam";
        public int DurationMinutes { get; set; } = 60;
        public int Capacity { get; set; } = 1;
        public ScheduleDeliveryType DeliveryType { get; set; } = ScheduleDeliveryType.Zoom;
        public string? MeetingUrl { get; set; }
        public string? Location { get; set; }
        public int BookingOpenDaysBefore { get; set; } = 30;
        public int BookingCloseHoursBefore { get; set; } = 1;
    }

    public sealed record ScheduleCalendarItem(
        int Id,
        int? CourseClassId,
        string Title,
        DateTime Start,
        DateTime End,
        string CourseName,
        string ClassName,
        string TeacherName,
        string TimeZoneId,
        ScheduleEventStatus Status,
        ScheduleDeliveryType DeliveryType,
        string? Location,
        int Capacity,
        int BookedCount,
        int SpotsRemaining,
        bool IsBooked,
        bool IsRecommended,
        bool CanBook,
        bool CanCancelBooking,
        bool CanJoin,
        string? JoinUrl,
        bool CanManage,
        string Category,
        string? UnavailableReason);

    public sealed record ScheduleOverviewData(
        int UpcomingCount,
        int BookedCount,
        int RecommendedCount,
        IReadOnlyList<ScheduleCalendarItem> UpcomingItems,
        IReadOnlyList<ScheduleCalendarItem> AvailableItems);

    public interface ISchedulingService
    {
        Task<ScheduleRosterPageData> GetRosterPageAsync(ScheduleActor actor, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ScheduleTeacherOption>> GetTeacherOptionsAsync(ScheduleActor actor, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ScheduleStudentOption>> GetStudentOptionsAsync(ScheduleActor actor, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ScheduleTeacherOption>> GetAvailableTeachersAsync(ScheduleActor actor, DateTime startLocal, string timeZoneId, int durationMinutes, CancellationToken cancellationToken = default);
        Task<OperationResult> SaveAvailabilityAsync(ScheduleActor actor, TeacherAvailabilityInput input, CancellationToken cancellationToken = default);
        Task<OperationResult> SaveRosterRuleAsync(ScheduleActor actor, ScheduleRosterRuleInput input, CancellationToken cancellationToken = default);
        Task<OperationResult> DeleteRosterRuleAsync(ScheduleActor actor, int ruleId, CancellationToken cancellationToken = default);
        Task<OperationResult> CreateManualEventAsync(ScheduleActor actor, ManualScheduleEventInput input, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ScheduleCalendarItem>> GetCalendarItemsAsync(ScheduleActor actor, DateTime rangeStartUtc, DateTime rangeEndUtc, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ScheduleCalendarItem>> GetStaffCalendarItemsAsync(ScheduleActor actor, string? teacherUserId, bool includeAllTeachers, DateTime rangeStartUtc, DateTime rangeEndUtc, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ScheduleCalendarItem>> GetStudentCalendarItemsAsync(ScheduleActor actor, string studentUserId, DateTime rangeStartUtc, DateTime rangeEndUtc, CancellationToken cancellationToken = default);
        Task<ScheduleOverviewData> GetOverviewAsync(ScheduleActor actor, CancellationToken cancellationToken = default);
        Task<ScheduleOverviewData> GetStaffOverviewAsync(ScheduleActor actor, string? teacherUserId, bool includeAllTeachers, CancellationToken cancellationToken = default);
        Task<ScheduleOverviewData> GetStudentOverviewAsync(ScheduleActor actor, string studentUserId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ScheduleCalendarItem>> GetCourseClassItemsAsync(ScheduleActor actor, int courseClassId, CancellationToken cancellationToken = default);
        Task<OperationResult> BookAsync(ScheduleActor actor, int scheduledEventId, CancellationToken cancellationToken = default);
        Task<OperationResult> CancelBookingAsync(ScheduleActor actor, int scheduledEventId, CancellationToken cancellationToken = default);
        Task<SchedulingJoinResult> GetJoinUrlAsync(ScheduleActor actor, int scheduledEventId, CancellationToken cancellationToken = default);
        Task<OperationResult> MoveEventAsync(ScheduleActor actor, int scheduledEventId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
        Task<OperationResult> CancelEventAsync(ScheduleActor actor, int scheduledEventId, string? reason, CancellationToken cancellationToken = default);
        Task GenerateUpcomingEventsAsync(int? ruleId = null, CancellationToken cancellationToken = default);
    }
}
