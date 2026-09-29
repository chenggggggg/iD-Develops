using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("CorrectAnswers")]
    public class CorrectAnswer
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public string Text { get; set; }
        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;

        public double? Score { get; set; }
        public bool IsDeleted { get; set; }
    }
}
