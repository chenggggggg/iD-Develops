using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("UserCreditLots")]
    public class UserCreditLot
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public int CreditTypeId { get; set; }

        public CreditType CreditType { get; set; } = null!;

        public int GrantedQuantity { get; set; }

        public int RemainingQuantity { get; set; }

        public CreditGrantScope Scope { get; set; } = CreditGrantScope.Global;

        public int? CourseId { get; set; }

        public Course? Course { get; set; }

        public int? CourseClassId { get; set; }

        public CourseClass? CourseClass { get; set; }

        public int? CatalogProductId { get; set; }

        public CatalogProduct? CatalogProduct { get; set; }

        public int? CatalogProductCreditGrantId { get; set; }

        public CatalogProductCreditGrant? CatalogProductCreditGrant { get; set; }

        [MaxLength(255)]
        public string? ExternalReference { get; set; }

        public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ExpiresAtUtc { get; set; }

        public ICollection<EventBookingCreditAllocation> BookingAllocations { get; set; } = new List<EventBookingCreditAllocation>();

        public ICollection<UserCreditTransaction> Transactions { get; set; } = new List<UserCreditTransaction>();
    }
}
