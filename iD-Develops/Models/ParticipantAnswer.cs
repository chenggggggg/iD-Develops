using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ParticipantAnswers")]
    public class ParticipantAnswer
    {
        public int Id { get; set; }
        public Guid ParticipantId { get; set; }
        public DateTime Timestamp { get; set; }
        public string AnswerText { get; set; }
        // Foreign key to associate with Question
        public int QuestionId { get; set; }
        public Question Question { get; set; }
        public bool IsDeleted { get; set; }
        public Guid RecordId { get; set; }
        public Record Record { get; set; }
        [NotMapped]
        public bool IsCorrect { get; set; }
    }
}
