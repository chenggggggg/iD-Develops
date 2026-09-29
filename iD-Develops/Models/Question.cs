using iD_Develops.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("Questions")]
    public abstract class Question
    {
        public int Id { get; set; }
        public int? ExamId { get; set; }
        public int QuestionNumber { get; set; }
        public string? MessageBeforeQuestion { get; set; }
        public string? ImageReference { get; set; }
        public string? AudioReference { get; set; }
        public string Text { get; set; }
        public string? Scenario { get; set; }
        public string? Feedback { get; set; }
        public string? FunFact { get; set; }
        public double? Score { get; set; }
        public ICollection<CorrectAnswer> CorrectAnswers { get; set; } = new List<CorrectAnswer>();  // ICollection in case of multiple correct answers i.e. Multiple Choice question with multiple correct answers.
        public bool IsDeleted { get; set; }
    }
}
