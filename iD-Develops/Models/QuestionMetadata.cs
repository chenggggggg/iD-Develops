using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [NotMapped]
    public class QuestionMetadata
    {
        public int QuestionNumber { get; set; }
        public int QuestionId { get; set; }
    }
}