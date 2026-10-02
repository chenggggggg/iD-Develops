using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CourseSectionExams")]
    public sealed class CourseSectionExam
    {
        public int Id { get; set; }

        public int CourseSectionId { get; set; }

        public CourseSection CourseSection { get; set; } = null!;

        public int ExamId { get; set; }

        public Exam Exam { get; set; } = null!;

        public int OrderNumber { get; set; }

        public int? UnlockAfterValue { get; set; }

        public CourseUnlockUnit? UnlockAfterUnit { get; set; }

        public bool IsRequiredForCompletion { get; set; } = true;

        [Range(0, double.MaxValue)]
        public double MinimumPassingScore { get; set; }

        public CourseExamFailureAction FailureAction { get; set; } = CourseExamFailureAction.RequirePassingScore;
    }
}
