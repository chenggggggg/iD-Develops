//using iD_Develops.Utilities;
//using Newtonsoft.Json;
//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;

//namespace iD_Develops.Models
//{
//    [Table("ExamQuestions")]
//    public class ExamQuestion
//    {
//        public int ExamId { get; set; }
//        [Required]
//        [JsonIgnore]
//        public Exam Exam { get; set; }

//        public int QuestionId { get; set; }
//        [Required]
//        [JsonConverter(typeof(QuestionConverter))]
//        public Question Question { get; set; }
//    }
//}
