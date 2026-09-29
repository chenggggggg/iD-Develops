using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("AssignmentSupportingFiles")]
    public class AssignmentSupportingFile
    {
        public int Id { get; set; }
        public int CourseAssignmentId { get; set; }
        public CourseAssignment CourseAssignment { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(1024)]
        public string FileReference { get; set; } = string.Empty;

        public int OrderNumber { get; set; }
    }
}
