using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("CatalogProductInvites")]
    public class CatalogProductInvite
    {
        public int Id { get; set; }

        public int CatalogProductId { get; set; }

        public CatalogProduct CatalogProduct { get; set; } = null!;

        [Required]
        [MaxLength(64)]
        public string Token { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Label { get; set; }

        [MaxLength(200)]
        public string? AllowedEmail { get; set; }

        public int MaxUses { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        public DateTime? ExpiresAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<CatalogProductInviteUse> Uses { get; set; } = new List<CatalogProductInviteUse>();
    }
}
