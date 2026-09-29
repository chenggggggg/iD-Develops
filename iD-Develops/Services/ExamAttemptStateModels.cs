using iD_Develops.Models;

namespace iD_Develops.Services
{
    public sealed class ExamAttemptPageLoadResult
    {
        public bool Exists { get; init; }
        public bool UserMatches { get; init; }
        public int ExamId { get; init; }
        public int? ExamVersionId { get; init; }
        public ExamStatus ExamStatus { get; init; } = ExamStatus.Invalid;
        public DateTime? EndDateTime { get; init; }
        public int CurrentQuestionId { get; init; }
        public Question? CurrentQuestion { get; init; }
        public string? CurrentSavedAnswerText { get; init; }
        public List<QuestionMetadata> QuestionsMetadata { get; init; } = new();
    }

    public sealed class ExamAttemptQuestionStateResult
    {
        public bool Exists { get; init; }
        public bool UserMatches { get; init; }
        public int ExamId { get; init; }
        public int? ExamVersionId { get; init; }
        public ExamStatus ExamStatus { get; init; } = ExamStatus.Invalid;
        public Question? Question { get; init; }
        public string? SavedAnswerText { get; init; }
    }
}
