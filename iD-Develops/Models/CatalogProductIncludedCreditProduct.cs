using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("CatalogProductIncludedCreditProducts")]
    public sealed class CatalogProductIncludedCreditProduct
    {
        public int CatalogProductId { get; set; }
        public CatalogProduct CatalogProduct { get; set; } = null!;

        public int IncludedCreditProductId { get; set; }
        public CatalogProduct IncludedCreditProduct { get; set; } = null!;
    }
}
