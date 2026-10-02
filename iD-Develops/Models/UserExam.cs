using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("UserExams")]
    public class UserExam
    {
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser ApplicationUser { get; set; } = null!;

        public int ExamId { get; set; }
        public Exam Exam { get; set; } = null!;

        public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

        public string? AssignedByUserId { get; set; }

        public ApplicationUser? AssignedByUser { get; set; }

        public DateTime? UnlockAtUtc { get; set; }

        public DateTime? DueAtUtc { get; set; }
    }
}
