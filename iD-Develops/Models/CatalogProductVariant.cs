using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("CatalogProductVariants")]
    public class CatalogProductVariant
    {
        public int Id { get; set; }

        public int CatalogProductId { get; set; }

        public CatalogProduct CatalogProduct { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "EUR";

        [MaxLength(200)]
        public string? StripePriceId { get; set; }

        public bool IsDefault { get; set; }

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }
    }
}
