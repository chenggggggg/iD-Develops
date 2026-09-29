using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ExamVersionCorrectAnswers")]
    public class ExamVersionCorrectAnswer
    {
        public int Id { get; set; }
        public int ExamVersionQuestionId { get; set; }
        public ExamVersionQuestion ExamVersionQuestion { get; set; } = null!;
        [Required]
        public string Text { get; set; } = string.Empty;
        public double? Score { get; set; }
    }
}
