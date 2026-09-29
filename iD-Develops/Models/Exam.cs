using iD_Develops.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace iD_Develops.Models
{
    [Table("Exams")]
    public class Exam
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [BindNever]
        [Required]
        public string CreatedByUserId { get; set; }

        [NotMapped]
        public DifficultyLevel Difficulty
        {
            get => (DifficultyLevel)DifficultyValue;
            set => DifficultyValue = (int)value;
        }

        [Required]
        [Column("Difficulty")]
        public int DifficultyValue { get; set; }

        [JsonIgnore]
        public ICollection<UserExam>? UserExams { get; set; }

        public ICollection<Question> Questions { get; set; } = new List<Question>();
        public ICollection<Record> Records { get; set; } = new List<Record>();
        public ICollection<ExamVersion> Versions { get; set; } = new List<ExamVersion>();
        public ICollection<ExamGradeBand> GradeBands { get; set; } = new List<ExamGradeBand>();

        public int? TimeLimit { get; set; }

        [Required]
        public string IntroductionPrimaryLanguage { get; set; }

        public string? IntroductionSecondaryLanguage { get; set; }

        public bool IsDeleted { get; set; }

        public int? CourseId { get; set; }

        public Course? Course { get; set; }

        [MaxLength(100)]
        public string? PublicSlug { get; set; }

        public string? CompletionTextPrimary { get; set; }

        public string? CompletionTextSecondary { get; set; }

        [Required]
        public ResultGradeDisplayMode ResultGradeDisplayMode { get; set; } = ResultGradeDisplayMode.Score;

        public string? ResultGradeCustomTextPrimary { get; set; }

        public string? ResultGradeCustomTextSecondary { get; set; }

        [Required]
        public ExamPublishStatus PublishStatus { get; set; } = ExamPublishStatus.Draft;

        [Range(-1, int.MaxValue)]
        public int MaxAttempts { get; set; } = 1;
    }
}
