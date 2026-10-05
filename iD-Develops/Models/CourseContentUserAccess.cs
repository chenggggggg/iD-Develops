using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("CourseContentUserAccesses")]
    public sealed class CourseContentUserAccess
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public int CourseId { get; set; }

        public Course Course { get; set; } = null!;

        [Required]
        [MaxLength(24)]
        public string ContentKind { get; set; } = string.Empty;

        public int ContentId { get; set; }

        public DateTime UnlockAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public string? UpdatedByUserId { get; set; }

        public ApplicationUser? UpdatedByUser { get; set; }
    }
}
