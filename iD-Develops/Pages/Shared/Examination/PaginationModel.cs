using iD_Develops.Models;

namespace iD_Develops.Pages.Shared.Examination
{
    public sealed class PaginationModel
    {
        /// <summary>
        /// Your existing metadata collection (NOT a DTO).
        /// Example type: List<QuestionMetadata>
        /// </summary>
        public IReadOnlyList<QuestionMetadata> QuestionsMetadata { get; init; }

        /// <summary>
        /// Currently loaded / active question
        /// </summary>
        public int CurrentQuestionId { get; init; }

        /// <summary>
        /// Controls which buttons appear on the right
        /// </summary>
        public PaginationMode Mode { get; init; } = PaginationMode.Exam;

        public int ExamId { get; init; }

        /// <summary>
        /// Convenience flags (optional but nice for Razor)
        /// </summary>
        public bool IsExamMode => Mode == PaginationMode.Exam;
        public bool IsEditingMode => Mode == PaginationMode.Editing;
        public int MaxAttempts { get; set; }
        public int AttemptsLeft { get; set; }
        public Guid? InProgressRecordId { get; set; }
        public ExamPublishStatus PublishStatus { get; init; } = ExamPublishStatus.Draft;
        public bool IsPublished => PublishStatus == ExamPublishStatus.Published;
    }
}

