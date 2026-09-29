using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("CourseInstructors")]
    public class CourseInstructor
    {
        public int CourseId { get; set; }

        public Course Course { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

        public string? AssignedByUserId { get; set; }

        public ApplicationUser? AssignedByUser { get; set; }
    }
}
