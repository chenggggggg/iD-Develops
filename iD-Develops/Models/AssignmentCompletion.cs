using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("AssignmentCompletions")]
    public class AssignmentCompletion
    {
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser ApplicationUser { get; set; } = null!;

        public int CourseAssignmentId { get; set; }

        public CourseAssignment CourseAssignment { get; set; } = null!;

        public bool IsCompleted { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }
}
