using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("UserExams")]
    public class UserExam
    {
        public string UserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }

        public int ExamId { get; set; }
        public Exam Exam { get; set; }
    }
}
