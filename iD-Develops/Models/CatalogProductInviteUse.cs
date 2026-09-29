using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CatalogProductInviteUses")]
    public class CatalogProductInviteUse
    {
        public int Id { get; set; }

        public int CatalogProductInviteId { get; set; }

        public CatalogProductInvite CatalogProductInvite { get; set; } = null!;

        public CatalogInviteUseStatus Status { get; set; } = CatalogInviteUseStatus.Pending;

        [MaxLength(200)]
        public string? CustomerEmail { get; set; }

        [MaxLength(200)]
        public string? StripeSessionId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ExpiresAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public DateTime? CancelledAtUtc { get; set; }
    }
}
