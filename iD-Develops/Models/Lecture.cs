using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("Lectures")]
    public class Lecture
    {
        public int Id { get; set; }

        public int CourseSectionId { get; set; }

        public CourseSection CourseSection { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public int OrderNumber { get; set; }

        public LectureContentType ContentType { get; set; }

        public string? Description { get; set; }

        [MaxLength(1024)]
        public string? VideoReference { get; set; }

        public int? UnlockAfterValue { get; set; }

        public CourseUnlockUnit? UnlockAfterUnit { get; set; }

        public ICollection<LectureSourceFile> SourceFiles { get; set; } = new List<LectureSourceFile>();

        public ICollection<LectureCompletion> Completions { get; set; } = new List<LectureCompletion>();
    }
}
