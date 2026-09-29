using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ExamVersionQuestions")]
    public class ExamVersionQuestion
    {
        public int Id { get; set; }
        public int ExamVersionId { get; set; }
        public ExamVersion ExamVersion { get; set; } = null!;
        public int SourceQuestionId { get; set; }
        [Required]
        public string QuestionType { get; set; } = string.Empty;
        public int QuestionNumber { get; set; }
        public string? MessageBeforeQuestion { get; set; }
        public string? ImageReference { get; set; }
        public string? AudioReference { get; set; }
        [Required]
        public string Text { get; set; } = string.Empty;
        public string? Scenario { get; set; }
        public string? Feedback { get; set; }
        public string? FunFact { get; set; }
        public double? Score { get; set; }
        public string? AnswerA { get; set; }
        public string? AnswerB { get; set; }
        public string? AnswerC { get; set; }
        public string? AnswerD { get; set; }
        public ICollection<ExamVersionCorrectAnswer> CorrectAnswers { get; set; } = new List<ExamVersionCorrectAnswer>();
    }
}
