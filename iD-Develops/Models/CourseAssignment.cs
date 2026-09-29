using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("CourseAssignments")]
    public class CourseAssignment
    {
        public int Id { get; set; }
        public int CourseSectionId { get; set; }
        public CourseSection CourseSection { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public int OrderNumber { get; set; }
        public string? Description { get; set; }
        public int? EstimatedDurationMinutes { get; set; }
        public string? Instructions { get; set; }

        [MaxLength(1024)]
        public string? InstructionalVideoReference { get; set; }

        public ICollection<AssignmentSupportingFile> SupportingFiles { get; set; } = new List<AssignmentSupportingFile>();

        public ICollection<AssignmentCompletion> Completions { get; set; } = new List<AssignmentCompletion>();
    }
}
