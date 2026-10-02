using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CourseSections")]
    public class CourseSection
    {
        public int Id { get; set; }

        public int CourseId { get; set; }

        public Course Course { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public int OrderNumber { get; set; }

        public int? UnlockAfterValue { get; set; }

        public CourseUnlockUnit? UnlockAfterUnit { get; set; }

        public ICollection<Lecture> Lectures { get; set; } = new List<Lecture>();

        public ICollection<CourseAssignment> Assignments { get; set; } = new List<CourseAssignment>();

        public ICollection<CourseClass> Classes { get; set; } = new List<CourseClass>();

        public ICollection<CourseSectionExam> Exams { get; set; } = new List<CourseSectionExam>();

        public ICollection<CourseSectionUserAccess> UserAccesses { get; set; } = new List<CourseSectionUserAccess>();
    }
}
