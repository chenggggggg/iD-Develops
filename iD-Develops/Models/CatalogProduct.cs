using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CatalogProducts")]
    public class CatalogProduct
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Slug { get; set; } = string.Empty;

        [MaxLength(4000)]
        public string? Summary { get; set; }

        public string? Description { get; set; }

        public string? FullDescriptionHtml { get; set; }

        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        public CatalogProductType ProductType { get; set; }

        public CatalogWorkflowType WorkflowType { get; set; }

        public CatalogProductStatus Status { get; set; } = CatalogProductStatus.Draft;

        public bool IsSalesActive { get; set; } = true;

        [MaxLength(3)]
        public string Currency { get; set; } = "EUR";

        [Column(TypeName = "decimal(18,2)")]
        public decimal? BasePrice { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? VatPercentage { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? RegistrationFee { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TransactionFee { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ServiceFee { get; set; }

        [MaxLength(500)]
        public string? ExternalBookingUrl { get; set; }

        [MaxLength(100)]
        public string? ExternalBookingButtonText { get; set; }

        [MaxLength(200)]
        public string? ConfirmationEmailSubject { get; set; }

        public string? ConfirmationEmailBodyHtml { get; set; }

        [MaxLength(200)]
        public string? OwnerNotificationSubject { get; set; }

        public string? OwnerNotificationBodyHtml { get; set; }

        public bool AllowsMultipleParticipants { get; set; }

        public int MaxParticipants { get; set; } = 1;

        public bool HideFromProductsPage { get; set; }

        public bool RequireAccessToken { get; set; }

        public bool EnableQuantity { get; set; }

        public int MinQuantity { get; set; } = 1;

        public int MaxQuantity { get; set; } = 1;

        public bool RequiresAccountCreation { get; set; }

        public int? GrantedCourseId { get; set; }

        public Course? GrantedCourse { get; set; }

        [MaxLength(150)]
        public string? IncludedBookingBenefitLabel { get; set; }

        [MaxLength(500)]
        public string? IncludedBookingBenefitUrl { get; set; }

        public bool IsFeatured { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<CatalogProductVariant> Variants { get; set; } = new List<CatalogProductVariant>();

        public ICollection<CatalogProductFormField> FormFields { get; set; } = new List<CatalogProductFormField>();

        public ICollection<CatalogProductInvite> Invites { get; set; } = new List<CatalogProductInvite>();

        public ICollection<CatalogProductCreditGrant> CreditGrants { get; set; } = new List<CatalogProductCreditGrant>();

        public ICollection<CatalogProductIncludedCreditProduct> IncludedCreditProducts { get; set; } = new List<CatalogProductIncludedCreditProduct>();

        public ICollection<CatalogProductIncludedCreditProduct> IncludedByProducts { get; set; } = new List<CatalogProductIncludedCreditProduct>();

        [NotMapped]
        public decimal? DisplayPrice => Variants
            .Where(v => v.IsActive)
            .OrderBy(v => v.SortOrder)
            .Select(v => (decimal?)v.Price)
            .FirstOrDefault() ?? BasePrice;
    }
}
