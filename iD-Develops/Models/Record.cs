using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("Records")]
    public class Record
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? UserId { get; set; }
        public ApplicationUser User { get; set; }
        public int ExamId { get; set; }
        public Exam Exam { get; set; }
        public int? ExamVersionId { get; set; }
        public ExamVersion? ExamVersion { get; set; }
        public double Score { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public ExamStatus ExamStatus { get; set; }
        public RecordStatusReason StatusReason { get; set; } = RecordStatusReason.None;
        public DateTime? StatusChangedAtUtc { get; set; }
        public DateTime? LastActivityUtc { get; set; }
        public ICollection<ParticipantAnswer> ParticipantAnswers { get; set; }
        public bool IsDeleted { get; set; }
    }
}
