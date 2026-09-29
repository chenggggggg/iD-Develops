using iD_Develops.Models;

namespace iD_Develops.Services
{
    public sealed class ExamAttemptStateService : IExamAttemptStateService
    {
        private readonly IRecordService _recordService;
        private readonly IQuestionService _questionService;
        private readonly IExamVersionService _examVersionService;
        private readonly IParticipantAnswerService _participantAnswerService;

        public ExamAttemptStateService(
            IRecordService recordService,
            IQuestionService questionService,
            IExamVersionService examVersionService,
            IParticipantAnswerService participantAnswerService)
        {
            _recordService = recordService;
            _questionService = questionService;
            _examVersionService = examVersionService;
            _participantAnswerService = participantAnswerService;
        }

        public async Task<ExamAttemptPageLoadResult> LoadPageAsync(
            int? examId,
            Guid recordId,
            int requestedQuestionId,
            string? userId = null,
            bool requireOwnership = false,
            CancellationToken cancellationToken = default)
        {
            var access = await _recordService.GetAttemptAccessStateAsync(
                recordId,
                examId,
                requireOwnership ? userId : null,
                cancellationToken);

            if (!access.Exists || (requireOwnership && !access.UserMatches))
            {
                return new ExamAttemptPageLoadResult
                {
                    Exists = access.Exists,
                    UserMatches = access.UserMatches,
                    ExamId = access.ExamId,
                    ExamVersionId = access.ExamVersionId,
                    ExamStatus = access.ExamStatus,
                    EndDateTime = access.EndDateTime
                };
            }

            if (access.ExamStatus != ExamStatus.InProgress)
            {
                return new ExamAttemptPageLoadResult
                {
                    Exists = true,
                    UserMatches = access.UserMatches,
                    ExamId = access.ExamId,
                    ExamVersionId = access.ExamVersionId,
                    ExamStatus = access.ExamStatus,
                    EndDateTime = access.EndDateTime
                };
            }

            var effectiveExamId = access.ExamId;
            if (effectiveExamId <= 0)
            {
                return new ExamAttemptPageLoadResult
                {
                    Exists = false,
                    UserMatches = access.UserMatches,
                    ExamStatus = ExamStatus.Invalid
                };
            }

            var questionsMetadata = access.ExamVersionId.HasValue
                ? await _examVersionService.GetQuestionMetadataAsync(effectiveExamId, access.ExamVersionId)
                : await _questionService.GetQuestionMetadataByExamIdAsync(effectiveExamId);
            var resolvedQuestionId = requestedQuestionId == 0 && questionsMetadata.Count > 0
                ? questionsMetadata[0].QuestionId
                : requestedQuestionId;

            if (resolvedQuestionId == 0)
            {
                return new ExamAttemptPageLoadResult
                {
                    Exists = true,
                    UserMatches = access.UserMatches,
                    ExamId = effectiveExamId,
                    ExamStatus = access.ExamStatus,
                    EndDateTime = access.EndDateTime,
                    QuestionsMetadata = questionsMetadata
                };
            }

            var question = access.ExamVersionId.HasValue
                ? await _examVersionService.GetQuestionForTakeAsync(effectiveExamId, resolvedQuestionId, access.ExamVersionId)
                : await _questionService.GetQuestionByIdWithoutSensitiveInfoAsync(effectiveExamId, resolvedQuestionId);
            var answerText = await _participantAnswerService.GetParticipantAnswerTextAsync(resolvedQuestionId, recordId);

            return new ExamAttemptPageLoadResult
            {
                Exists = true,
                UserMatches = access.UserMatches,
                ExamId = effectiveExamId,
                ExamVersionId = access.ExamVersionId,
                ExamStatus = access.ExamStatus,
                EndDateTime = access.EndDateTime,
                CurrentQuestionId = resolvedQuestionId,
                CurrentQuestion = question,
                CurrentSavedAnswerText = answerText,
                QuestionsMetadata = questionsMetadata
            };
        }

        public async Task<ExamAttemptQuestionStateResult> LoadQuestionAsync(
            int? examId,
            Guid recordId,
            int questionId,
            string? userId = null,
            bool requireOwnership = false,
            CancellationToken cancellationToken = default)
        {
            if (questionId <= 0)
            {
                return new ExamAttemptQuestionStateResult
                {
                    ExamStatus = ExamStatus.Invalid
                };
            }

            var access = await _recordService.GetAttemptAccessStateAsync(
                recordId,
                examId,
                requireOwnership ? userId : null,
                cancellationToken);

            if (!access.Exists || (requireOwnership && !access.UserMatches))
            {
                return new ExamAttemptQuestionStateResult
                {
                    Exists = access.Exists,
                    UserMatches = access.UserMatches,
                    ExamId = access.ExamId,
                    ExamVersionId = access.ExamVersionId,
                    ExamStatus = access.ExamStatus
                };
            }

            if (access.ExamStatus != ExamStatus.InProgress)
            {
                return new ExamAttemptQuestionStateResult
                {
                    Exists = true,
                    UserMatches = access.UserMatches,
                    ExamId = access.ExamId,
                    ExamVersionId = access.ExamVersionId,
                    ExamStatus = access.ExamStatus
                };
            }

            var effectiveExamId = access.ExamId;
            if (effectiveExamId <= 0)
            {
                return new ExamAttemptQuestionStateResult
                {
                    Exists = false,
                    UserMatches = access.UserMatches,
                    ExamStatus = ExamStatus.Invalid
                };
            }

            var question = access.ExamVersionId.HasValue
                ? await _examVersionService.GetQuestionForTakeAsync(effectiveExamId, questionId, access.ExamVersionId)
                : await _questionService.GetQuestionByIdWithoutSensitiveInfoAsync(effectiveExamId, questionId);
            var answerText = await _participantAnswerService.GetParticipantAnswerTextAsync(questionId, recordId);

            return new ExamAttemptQuestionStateResult
            {
                Exists = true,
                UserMatches = access.UserMatches,
                ExamId = effectiveExamId,
                ExamVersionId = access.ExamVersionId,
                ExamStatus = access.ExamStatus,
                Question = question,
                SavedAnswerText = answerText
            };
        }
    }
}
