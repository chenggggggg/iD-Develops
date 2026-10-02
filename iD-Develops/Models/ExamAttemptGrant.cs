using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ExamAttemptGrants")]
    public sealed class ExamAttemptGrant
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public int ExamId { get; set; }

        public Exam Exam { get; set; } = null!;

        [Range(1, 1000)]
        public int AdditionalAttempts { get; set; } = 1;

        public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;

        public string? GrantedByUserId { get; set; }

        public ApplicationUser? GrantedByUser { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
