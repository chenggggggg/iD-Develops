using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ExamLogs")]
    public class ExamLog
    {
        [Required]
        public int Id { get; set; }
        [Required]
        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }
        [Required]
        public DateTime Timestamp { get; set; }
        [Required]
        public int ExamId { get; set; }
        public Exam Exam { get; set; }
        [Required]
        public int QuestionId { get; set; }
        public Question Question { get; set; }
        [Required]
        public string Action { get; set; }
        [Required]
        public string LogMessage { get; set; }
        [Required]
        public string AnswerText { get; set; }
    }
}