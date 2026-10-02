using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("CourseSectionUserAccesses")]
    public sealed class CourseSectionUserAccess
    {
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public int CourseSectionId { get; set; }

        public CourseSection CourseSection { get; set; } = null!;

        public DateTime? UnlockAtUtc { get; set; }

        public bool IsManualOverride { get; set; }

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public string? UpdatedByUserId { get; set; }

        public ApplicationUser? UpdatedByUser { get; set; }
    }
}
