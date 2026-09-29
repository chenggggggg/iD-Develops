using iD_Develops.Models;

namespace iD_Develops.Services
{
    public interface IQuestionService
    {
        Task<Question?> GetQuestionByIdWithoutSensitiveInfoAsync(int examId, int questionId);
        Task<List<QuestionMetadata>> GetQuestionMetadataByExamIdAsync(int examId);
        Task<List<Question>> GetQuestionsForExamForEditAsync(int examId);
        Task<Question?> GetQuestionByNumberForUpdateAsync(int examId, int questionNumber);
        Task AddQuestionAsync(int examId, Question question);
        // Tracked entity (for updates)
        Task<Question?> GetQuestionForExamForUpdateAsync(int examId, int questionId);
        Task<Question?> GetQuestionForExamForEditAsync(int examId, int questionId);
        Task UpdateQuestionAsync(Question question);
        Task ApplyAutosavePatchAsync(
            Question trackedQuestion,
            string? text,
            string? messageBeforeQuestion,
            string? scenario,
            string? feedback,
            string? funFact,
            double? score,
            string? openCorrectAnswerText,
            string? multipleChoiceAnswerA,
            string? multipleChoiceAnswerB,
            string? multipleChoiceAnswerC,
            string? multipleChoiceAnswerD,
            string? multipleChoiceCorrect,
            string? trueOrFalseCorrect,
            string? imageReference,
            string? audioReference);
        Task DeleteQuestionFromExamAsync(int examId, int questionId);
    }
}


