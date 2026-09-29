using iD_Develops.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ExamVersions")]
    public class ExamVersion
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public Exam Exam { get; set; } = null!;
        public int VersionNumber { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        public int DifficultyValue { get; set; }
        public int? TimeLimit { get; set; }
        [Required]
        public string IntroductionPrimaryLanguage { get; set; } = string.Empty;
        public string? IntroductionSecondaryLanguage { get; set; }
        public string? CompletionTextPrimary { get; set; }
        public string? CompletionTextSecondary { get; set; }
        [Required]
        public ResultGradeDisplayMode ResultGradeDisplayMode { get; set; } = ResultGradeDisplayMode.Score;
        public string? ResultGradeCustomTextPrimary { get; set; }
        public string? ResultGradeCustomTextSecondary { get; set; }
        public int MaxAttempts { get; set; }
        public string? PublicSlug { get; set; }
        public string? CourseName { get; set; }
        public DateTime PublishedAtUtc { get; set; }
        public ICollection<Record> Records { get; set; } = new List<Record>();
        public ICollection<ExamVersionQuestion> Questions { get; set; } = new List<ExamVersionQuestion>();
        public ICollection<ExamVersionGradeBand> GradeBands { get; set; } = new List<ExamVersionGradeBand>();
    }
}
