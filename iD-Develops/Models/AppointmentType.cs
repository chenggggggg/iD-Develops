using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("AppointmentTypes")]
    public class AppointmentType
    {
        public int Id { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Range(5, 480)]
        public int DurationMinutes { get; set; } = 60;

        public int RequiredCreditTypeId { get; set; }
        public CreditType RequiredCreditType { get; set; } = null!;

        [Range(1, 1000)]
        public int CreditCost { get; set; } = 1;

        public int CreditConsumptionPolicyId { get; set; }
        public CreditConsumptionPolicy CreditConsumptionPolicy { get; set; } = null!;

        [Required]
        public string CreatedByUserId { get; set; } = string.Empty;
        public ApplicationUser CreatedByUser { get; set; } = null!;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<AppointmentTypeTeacher> Teachers { get; set; } = new List<AppointmentTypeTeacher>();
        public ICollection<ScheduledEvent> ScheduledEvents { get; set; } = new List<ScheduledEvent>();
    }
}
