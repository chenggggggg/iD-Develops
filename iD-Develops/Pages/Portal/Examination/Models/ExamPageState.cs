using iD_Develops.Models;
using iD_Develops.Pages.Shared.Examination;

namespace iD_Develops.Pages.Portal.Examination.Models
{
    public sealed class ExamPageState
    {
        public int ExamId { get; init; }
        public Guid RecordId { get; init; }
        public int QuestionId { get; init; }

        public ExamStatus ExamStatus { get; init; }

        public Question? CurrentQuestion { get; init; }
        public string? CurrentSavedAnswerText { get; init; }
        public List<QuestionMetadata> QuestionsMetadata { get; init; } = new();

        public ExaminationHeaderModel Header { get; init; } = new();
    }
}
