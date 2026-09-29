using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [NotMapped]
    public class ExamResults
    {
        public List<Question> Questions { get; set; }
        public List<ParticipantAnswer> ParticipantAnswers { get; set; }
        public double Score { get; set; }
        public double MaxScore { get; set; }
    }
}
