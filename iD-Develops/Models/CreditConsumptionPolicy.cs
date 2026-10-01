using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CreditConsumptionPolicies")]
    public class CreditConsumptionPolicy
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string NormalizedName { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public CreditConsumptionTiming ConsumptionTiming { get; set; } = CreditConsumptionTiming.OnBooking;

        public int CancellationWindowHours { get; set; } = 24;

        public CreditResolutionAction AttendedAction { get; set; } = CreditResolutionAction.Consume;

        public CreditResolutionAction NoShowAction { get; set; } = CreditResolutionAction.Consume;

        public CreditResolutionAction EarlyCancellationAction { get; set; } = CreditResolutionAction.Return;

        public CreditResolutionAction LateCancellationAction { get; set; } = CreditResolutionAction.Consume;

        public CreditResolutionAction StaffCancellationAction { get; set; } = CreditResolutionAction.Return;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<CourseClass> CourseClasses { get; set; } = new List<CourseClass>();

        public ICollection<ScheduledEvent> ScheduledEvents { get; set; } = new List<ScheduledEvent>();

        public ICollection<AppointmentType> AppointmentTypes { get; set; } = new List<AppointmentType>();

        public ICollection<CatalogProduct> CatalogProducts { get; set; } = new List<CatalogProduct>();
    }
}
