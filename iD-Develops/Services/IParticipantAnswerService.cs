using iD_Develops.Models;

namespace iD_Develops.Services
{
    public interface IParticipantAnswerService
    {
        Task<ParticipantAnswer?> GetParticipantAnswerByRecordIdAsync(int questionId, Guid recordId);
        Task<string?> GetParticipantAnswerTextAsync(int questionId, Guid recordId);
        Task<List<ParticipantAnswer>> GetParticipantAnswersByRecordIdAsync(Guid recordId);
        Task<bool> SaveParticipantAnswerAsync(ParticipantAnswer participantAnswer);
        Task<List<int>> GetAnsweredQuestionsAsync(Guid recordId);
    }
}
