using iD_Develops.Pages.Shared.Examination;

namespace iD_Develops.Pages.Portal.Examination.Models
{
    public sealed class StartPageState
    {
        public int ExamId { get; set; }
        public string IntroductionPrimary { get; set; }
        public string IntroductionSecondary { get; set; }
        public int? TimeLimit { get; set; }
        public ExaminationHeaderModel Header { get; set; }
    }
}
