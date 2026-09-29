using System.ComponentModel.DataAnnotations;
using iD_Develops.Enums;

namespace iD_Develops.Pages.Portal.Examination.Models
{
    public class CreateExamInputModel
    {
        public const int MinimumGradeBandRows = 2;

        [Required(ErrorMessage = "Give this exam a name before continuing.")]
        public string Name { get; set; } = "";

        [Range(1, int.MaxValue, ErrorMessage = "Leave this empty for no time limit, or enter at least 1 minute.")]
        public int? TimeLimit { get; set; }

        public string? IntroductionPrimaryLanguage { get; set; }

        public string? IntroductionSecondaryLanguage { get; set; }

        [Required(ErrorMessage = "Choose the level test for this exam.")]
        [Range(0, int.MaxValue, ErrorMessage = "Choose the level test for this exam.")]
        public int? DifficultyValue { get; set; }
        public string? CompletionTextPrimary { get; set; }

        [StringLength(100)]
        public string? PublicSlug { get; set; }

        public string? CompletionTextSecondary { get; set; }
        public ResultGradeDisplayMode ResultGradeDisplayMode { get; set; } = ResultGradeDisplayMode.Score;
        public string? ResultGradeCustomTextPrimary { get; set; }
        public string? ResultGradeCustomTextSecondary { get; set; }
        public List<ResultGradeBandInputModel> ResultGradeBands { get; set; } = CreateEmptyGradeBands();
        [Range(1, int.MaxValue, ErrorMessage = "Leave this empty for unlimited attempts, or enter at least 1 attempt.")]
        public int? MaxAttempts { get; set; }
        public bool DisplayGradeOnResults
        {
            get => ResultGradeDisplayMode != ResultGradeDisplayMode.Score;
            set => ResultGradeDisplayMode = value ? ResultGradeDisplayMode.GradeBands : ResultGradeDisplayMode.Score;
        }

        public void EnsureMinimumGradeBandRows()
        {
            while (ResultGradeBands.Count < MinimumGradeBandRows)
                ResultGradeBands.Add(new ResultGradeBandInputModel());
        }

        private static List<ResultGradeBandInputModel> CreateEmptyGradeBands()
        {
            var rows = new List<ResultGradeBandInputModel>();
            for (var i = 0; i < MinimumGradeBandRows; i++)
                rows.Add(new ResultGradeBandInputModel());
            return rows;
        }
    }
}
