using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Pages.Portal.Examination.Models
{
    public class ResultGradeBandInputModel
    {
        [Range(0, double.MaxValue, ErrorMessage = "Upper score must be 0 or higher.")]
        public double? MaximumScore { get; set; }

        [StringLength(100, ErrorMessage = "Grade name must be 100 characters or fewer.")]
        public string? LabelPrimary { get; set; }
    }
}
