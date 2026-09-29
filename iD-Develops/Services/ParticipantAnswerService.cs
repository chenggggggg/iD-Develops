using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;

namespace iD_Develops.Services
{
    public class ParticipantAnswerService : IParticipantAnswerService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly RecordLifecycleOptions _lifecycleOptions;

        public ParticipantAnswerService(
            ApplicationDbContext dbContext,
            IOptions<RecordLifecycleOptions> lifecycleOptions)
        {
            _dbContext = dbContext;
            _lifecycleOptions = lifecycleOptions.Value ?? new RecordLifecycleOptions();
        }

        public async Task<ParticipantAnswer?> GetParticipantAnswerByRecordIdAsync(int questionId, Guid recordId)
        {
            return await _dbContext.ParticipantAnswers
                .AsNoTracking()
                .Where(pa => pa.QuestionId == questionId && pa.RecordId == recordId && !pa.IsDeleted)
                .OrderByDescending(pa => pa.Timestamp)
                .FirstOrDefaultAsync();
        }

        public async Task<string?> GetParticipantAnswerTextAsync(int questionId, Guid recordId)
        {
            return await _dbContext.ParticipantAnswers
                .AsNoTracking()
                .Where(pa => pa.QuestionId == questionId && pa.RecordId == recordId && !pa.IsDeleted)
                .OrderByDescending(pa => pa.Timestamp)
                .Select(pa => pa.AnswerText)
                .FirstOrDefaultAsync();
        }

        public async Task<List<ParticipantAnswer>> GetParticipantAnswersByRecordIdAsync(Guid recordId)
        {
            return await _dbContext.ParticipantAnswers
                .AsNoTracking()
                .Where(p => p.RecordId == recordId && !p.IsDeleted)
                .ToListAsync();
        }

        public async Task<bool> SaveParticipantAnswerAsync(ParticipantAnswer participantAnswer)
        {
            if (participantAnswer == null)
                throw new ArgumentNullException(nameof(participantAnswer));

            var graceSeconds = _lifecycleOptions.SubmissionGraceSeconds;
            if (graceSeconds < 0)
                graceSeconds = 0;

            var submittedAnswer = participantAnswer.AnswerText?.Trim() ?? string.Empty;
            var shouldClear = string.IsNullOrWhiteSpace(submittedAnswer);

            await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var record = await _dbContext.Records
                .FirstOrDefaultAsync(r =>
                    r.Id == participantAnswer.RecordId &&
                    !r.IsDeleted &&
                    r.ExamStatus == ExamStatus.InProgress);

            if (record == null)
                return false;

            if (record.EndDateTime.HasValue &&
                participantAnswer.Timestamp > record.EndDateTime.Value.AddSeconds(graceSeconds))
            {
                return false;
            }

            var existingAnswer = await _dbContext.ParticipantAnswers
                .FirstOrDefaultAsync(pa =>
                    pa.QuestionId == participantAnswer.QuestionId &&
                    pa.RecordId == participantAnswer.RecordId);

            var changed = false;
            if (existingAnswer == null)
            {
                if (!shouldClear)
                {
                    participantAnswer.AnswerText = submittedAnswer;
                    participantAnswer.IsDeleted = false;
                    _dbContext.ParticipantAnswers.Add(participantAnswer);
                    changed = true;
                }
            }
            else if (existingAnswer.Timestamp < participantAnswer.Timestamp)
            {
                existingAnswer.AnswerText = shouldClear ? string.Empty : submittedAnswer;
                existingAnswer.Timestamp = participantAnswer.Timestamp;
                existingAnswer.IsDeleted = shouldClear;
                changed = true;
            }

            if (record.LastActivityUtc == null || record.LastActivityUtc < participantAnswer.Timestamp)
            {
                record.LastActivityUtc = participantAnswer.Timestamp;
                changed = true;
            }

            if (!changed)
                return false;

            await _dbContext.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }

        public async Task<List<int>> GetAnsweredQuestionsAsync(Guid recordId)
        {
            return await _dbContext.ParticipantAnswers
                .AsNoTracking()
                .Where(pa =>
                    pa.RecordId == recordId &&
                    !pa.IsDeleted &&
                    pa.AnswerText != null &&
                    pa.AnswerText.Trim() != string.Empty)
                .Join(_dbContext.Questions.AsNoTracking(),
                      pa => pa.QuestionId,
                      q => q.Id,
                      (pa, q) => q.QuestionNumber)
                .OrderBy(qn => qn)
                .ToListAsync();
        }
    }
}
