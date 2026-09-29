using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("UserCourses")]
    public class UserCourse
    {
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser ApplicationUser { get; set; } = null!;

        public int CourseId { get; set; }

        public Course Course { get; set; } = null!;

        public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? PurchasedAtUtc { get; set; }

        public CourseAssignmentSource AssignmentSource { get; set; } = CourseAssignmentSource.Admin;

        public string? GrantedByUserId { get; set; }

        public ApplicationUser? GrantedByUser { get; set; }
    }
}
