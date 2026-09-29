using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("ScheduleRosterRules")]
    public class ScheduleRosterRule
    {
        public int Id { get; set; }

        [Required]
        public string TeacherUserId { get; set; } = string.Empty;

        public ApplicationUser TeacherUser { get; set; } = null!;

        public int CourseClassId { get; set; }

        public CourseClass CourseClass { get; set; } = null!;

        public DayOfWeek DayOfWeek { get; set; }

        public TimeOnly LocalStartTime { get; set; }

        [Required, MaxLength(100)]
        public string TimeZoneId { get; set; } = "Europe/Amsterdam";

        public DateOnly ActiveFromDate { get; set; }

        public DateOnly? ActiveUntilDate { get; set; }

        [Range(5, 1440)]
        public int DurationMinutes { get; set; } = 60;

        [Range(1, 10000)]
        public int Capacity { get; set; } = 1;

        public ScheduleDeliveryType DeliveryType { get; set; } = ScheduleDeliveryType.Zoom;

        [MaxLength(1024)]
        public string? MeetingUrl { get; set; }

        [MaxLength(300)]
        public string? Location { get; set; }

        [Range(0, 365)]
        public int BookingOpenDaysBefore { get; set; } = 30;

        [Range(0, 8760)]
        public int BookingCloseHoursBefore { get; set; } = 1;

        [Range(1, 52)]
        public int GenerateWeeksAhead { get; set; } = 12;

        public bool IsVisibleForStudentBooking { get; set; } = true;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<ScheduledEvent> ScheduledEvents { get; set; } = new List<ScheduledEvent>();
    }
}
