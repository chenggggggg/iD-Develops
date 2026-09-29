using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ExamVersionGradeBands")]
    public class ExamVersionGradeBand
    {
        public int Id { get; set; }
        public int ExamVersionId { get; set; }
        public ExamVersion ExamVersion { get; set; } = null!;

        [Range(0, double.MaxValue)]
        public double MinimumScore { get; set; }

        [Required]
        public string LabelPrimary { get; set; } = string.Empty;

        public string? LabelSecondary { get; set; }
    }
}
