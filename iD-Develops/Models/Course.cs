using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("Courses")]
    public class Course
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public string? CreatedByUserId { get; set; }

        public ApplicationUser? CreatedByUser { get; set; }

        public ICollection<UserCourse> UserCourses { get; set; } = new List<UserCourse>();

        public ICollection<CourseInstructor> Instructors { get; set; } = new List<CourseInstructor>();

        public ICollection<Exam> Exams { get; set; } = new List<Exam>();

        public ICollection<LearningMaterial> LearningMaterials { get; set; } = new List<LearningMaterial>();

        public ICollection<CourseSection> Sections { get; set; } = new List<CourseSection>();

        public ICollection<ScheduledEvent> ScheduledEvents { get; set; } = new List<ScheduledEvent>();

    }
}
