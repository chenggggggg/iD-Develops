using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("MultipleChoiceAnswers")]
    public class MultipleChoiceAnswer
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public string? AnswerA { get; set; }
        public string? AnswerB { get; set; }
        public string? AnswerC { get; set; }
        public string? AnswerD { get; set; }
        public bool IsDeleted { get; set; }
    }
}
