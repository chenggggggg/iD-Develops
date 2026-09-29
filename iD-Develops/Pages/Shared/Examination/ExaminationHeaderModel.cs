using iD_Develops.Models;

namespace iD_Develops.Pages.Shared.Examination
{
    public sealed class ExaminationHeaderModel
    {
        public int? ExamId { get; init; }
        public string ExamTitle { get; init; } = string.Empty;
        public string CourseName { get; init; } = string.Empty;

        public int? TimeLimit { get; set; }
        /// <summary>
        /// Whether the countdown timer should be shown.
        /// </summary>
        public bool ShowTimer => TimeLimit.HasValue && TimeLimit.Value > 0;

        /// <summary>
        /// Absolute end time of the exam (UTC). Null = no time limit.
        /// </summary>
        public DateTime? EndDateTime { get; init; }

        public PaginationMode Mode { get; set; } = PaginationMode.Exam;
    }
}
