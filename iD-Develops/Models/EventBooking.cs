using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("EventBookings")]
    public class EventBooking
    {
        public int Id { get; set; }

        public int ScheduledEventId { get; set; }

        public ScheduledEvent ScheduledEvent { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public EventBookingStatus Status { get; set; } = EventBookingStatus.Confirmed;

        public DateTime BookedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CancelledAtUtc { get; set; }

        public ZoomRegistrationStatus ZoomRegistrationStatus { get; set; } = ZoomRegistrationStatus.NotRequired;

        [MaxLength(300)]
        public string? ZoomRegistrantId { get; set; }

        public string? ProtectedZoomJoinUrl { get; set; }

        public DateTime? ZoomRegistrationSyncedAtUtc { get; set; }

        [MaxLength(2000)]
        public string? ZoomRegistrationError { get; set; }

        public DateTime? FirstJoinedAtUtc { get; set; }

        public DateTime? LastLeftAtUtc { get; set; }

        public int ZoomAttendanceSeconds { get; set; }

        public DateTime? AttendanceReconciledAtUtc { get; set; }

        public AttendanceResolutionSource? AttendanceResolutionSource { get; set; }

        public bool AttendanceManuallyOverridden { get; set; }

        public DateTime? CreditResolvedAtUtc { get; set; }

        public CreditResolutionAction? CreditResolution { get; set; }

        public ICollection<EventBookingCreditAllocation> CreditAllocations { get; set; } = new List<EventBookingCreditAllocation>();

        public ICollection<ZoomAttendanceSegment> ZoomAttendanceSegments { get; set; } = new List<ZoomAttendanceSegment>();
    }
}
