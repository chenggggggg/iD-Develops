using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("ScheduledEvents")]
    public class ScheduledEvent
    {
        public int Id { get; set; }

        public int? ScheduleRosterRuleId { get; set; }

        public ScheduleRosterRule? ScheduleRosterRule { get; set; }

        public int? AppointmentTypeId { get; set; }

        public AppointmentType? AppointmentType { get; set; }

        public int? CourseClassId { get; set; }

        public CourseClass? CourseClass { get; set; }

        public int? CourseId { get; set; }

        public Course? Course { get; set; }

        [Required]
        public string TeacherUserId { get; set; } = string.Empty;

        public ApplicationUser TeacherUser { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string CourseNameSnapshot { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string ClassNameSnapshot { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string TeacherNameSnapshot { get; set; } = string.Empty;

        public DateTime StartAtUtc { get; set; }

        public DateTime EndAtUtc { get; set; }

        [Required, MaxLength(100)]
        public string TimeZoneId { get; set; } = "Europe/Amsterdam";

        public int Capacity { get; set; } = 1;

        public ScheduleEventStatus Status { get; set; } = ScheduleEventStatus.Scheduled;

        public ScheduleEventSource Source { get; set; } = ScheduleEventSource.Manual;

        public ScheduleDeliveryType DeliveryType { get; set; } = ScheduleDeliveryType.Zoom;

        [MaxLength(1024)]
        public string? MeetingUrl { get; set; }

        [MaxLength(100)]
        public string? ZoomMeetingId { get; set; }

        [MaxLength(300)]
        public string? ZoomMeetingUuid { get; set; }

        public DateTime? ZoomEndedAtUtc { get; set; }

        public DateTime? AttendanceReconciledAtUtc { get; set; }

        [MaxLength(2000)]
        public string? AttendanceReconciliationError { get; set; }

        [MaxLength(1024)]
        public string? GoogleCalendarEventId { get; set; }

        [MaxLength(1024)]
        public string? GoogleCalendarId { get; set; }

        public DateTime? ExternalSyncedAtUtc { get; set; }

        [MaxLength(2000)]
        public string? ExternalSyncError { get; set; }

        [MaxLength(300)]
        public string? Location { get; set; }

        public DateTime BookingOpensAtUtc { get; set; }

        public DateTime BookingClosesAtUtc { get; set; }

        public CourseClassBookingAccess BookingAccess { get; set; } = CourseClassBookingAccess.CourseEnrollment;

        public bool IsVisibleForStudentBooking { get; set; } = true;

        public int? RequiredCreditTypeId { get; set; }

        public CreditType? RequiredCreditType { get; set; }

        public int CreditCost { get; set; } = 1;

        public int? CreditConsumptionPolicyId { get; set; }

        public CreditConsumptionPolicy? CreditConsumptionPolicy { get; set; }

        public bool IsDetachedOverride { get; set; }

        [MaxLength(1000)]
        public string? CancellationReason { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<EventBooking> Bookings { get; set; } = new List<EventBooking>();
    }
}
