using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CreditTypes")]
    public class CreditType
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

        [Required]
        [MaxLength(80)]
        public string SingularLabel { get; set; } = "credit";

        [Required]
        [MaxLength(80)]
        public string PluralLabel { get; set; } = "credits";

        public int? DefaultValidityValue { get; set; }

        public CreditValidityUnit? DefaultValidityUnit { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<CatalogProductCreditGrant> ProductGrants { get; set; } = new List<CatalogProductCreditGrant>();

        public ICollection<CourseClass> CourseClasses { get; set; } = new List<CourseClass>();

        public ICollection<ScheduledEvent> ScheduledEvents { get; set; } = new List<ScheduledEvent>();

        public ICollection<AppointmentType> AppointmentTypes { get; set; } = new List<AppointmentType>();

        public ICollection<UserCreditLot> UserCreditLots { get; set; } = new List<UserCreditLot>();

        public ICollection<UserCreditTransaction> UserCreditTransactions { get; set; } = new List<UserCreditTransaction>();
    }
}
