using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("TeacherAvailabilityWindows")]
    public class TeacherAvailabilityWindow
    {
        public int Id { get; set; }

        [Required]
        public string TeacherUserId { get; set; } = string.Empty;

        public ApplicationUser TeacherUser { get; set; } = null!;

        public DayOfWeek DayOfWeek { get; set; }

        public TimeOnly LocalStartTime { get; set; }

        public TimeOnly LocalEndTime { get; set; }

        [Required, MaxLength(100)]
        public string TimeZoneId { get; set; } = "Europe/Amsterdam";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
