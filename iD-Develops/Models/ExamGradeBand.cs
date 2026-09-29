using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ExamGradeBands")]
    public class ExamGradeBand
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public Exam Exam { get; set; } = null!;

        [Range(0, double.MaxValue)]
        public double MinimumScore { get; set; }

        [Required]
        public string LabelPrimary { get; set; } = string.Empty;

        public string? LabelSecondary { get; set; }
    }
}
