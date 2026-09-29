using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("SchedulingProviderConnections")]
    public class SchedulingProviderConnection
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public SchedulingProvider Provider { get; set; }

        [Required]
        public string ProtectedAccessToken { get; set; } = string.Empty;

        public string? ProtectedRefreshToken { get; set; }

        public DateTime AccessTokenExpiresAtUtc { get; set; }

        [MaxLength(300)]
        public string? ProviderAccountId { get; set; }

        [MaxLength(320)]
        public string? ProviderEmail { get; set; }

        [MaxLength(2000)]
        public string? GrantedScopes { get; set; }

        [MaxLength(1024)]
        public string? CalendarId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? LastSuccessfulSyncAtUtc { get; set; }

        [MaxLength(2000)]
        public string? LastError { get; set; }
    }
}
