using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ZoomAttendanceSegments")]
    public class ZoomAttendanceSegment
    {
        public long Id { get; set; }

        public int EventBookingId { get; set; }

        public EventBooking EventBooking { get; set; } = null!;

        [Required, MaxLength(300)]
        public string ParticipantSessionId { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? ZoomMeetingUuid { get; set; }

        public DateTime JoinedAtUtc { get; set; }

        public DateTime? LeftAtUtc { get; set; }

        public int DurationSeconds { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
