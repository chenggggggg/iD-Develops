using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Models;

public class AppointmentBookingRequest
{
    public Guid Id { get; set; }
    [MaxLength(450)] public string RequestedByUserId { get; set; } = string.Empty;
    [MaxLength(450)] public string AttendeeUserId { get; set; } = string.Empty;
    [MaxLength(450)] public string TeacherUserId { get; set; } = string.Empty;
    public int AppointmentTypeId { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public int? ScheduledEventId { get; set; }
    public ScheduledEvent? ScheduledEvent { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "Preparing";
    [MaxLength(1000)] public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
