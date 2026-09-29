using iD_Develops.Models;
using Microsoft.AspNetCore.Mvc;

namespace iD_Develops.Services
{
    public sealed class ExamTakeFlowService : IExamTakeFlowService
    {
        private readonly IExamAttemptStateService _examAttemptStateService;
        private readonly IRecordService _recordService;
        private readonly IParticipantAnswerService _participantAnswerService;
        private readonly IExamVersionService _examVersionService;

        public ExamTakeFlowService(
            IExamAttemptStateService examAttemptStateService,
            IRecordService recordService,
            IParticipantAnswerService participantAnswerService,
            IExamVersionService examVersionService)
        {
            _examAttemptStateService = examAttemptStateService;
            _recordService = recordService;
            _participantAnswerService = participantAnswerService;
            _examVersionService = examVersionService;
        }

        public async Task<ExamTakePageData> LoadPageAsync(int? examId, Guid recordId, int questionId, string? userId = null, bool requireOwnership = false)
        {
            var attemptState = await _examAttemptStateService.LoadPageAsync(examId, recordId, questionId, userId, requireOwnership);
            if (!attemptState.Exists)
                return new ExamTakePageData { Exists = false };

            if (!attemptState.UserMatches)
                return new ExamTakePageData { Exists = true, UserMatches = false };

            return new ExamTakePageData
            {
                Exists = true,
                UserMatches = true,
                ExamId = attemptState.ExamId,
                QuestionId = attemptState.CurrentQuestionId,
                ExamVersionId = attemptState.ExamVersionId,
                ExamStatus = attemptState.ExamStatus,
                EndDateTime = attemptState.EndDateTime,
                CurrentQuestion = attemptState.CurrentQuestion,
                CurrentSavedAnswerText = attemptState.CurrentSavedAnswerText,
                QuestionsMetadata = attemptState.QuestionsMetadata,
                Descriptor = await _examVersionService.GetPublishedDescriptorAsync(attemptState.ExamId, attemptState.ExamVersionId)
            };
        }

        public async Task<ExamTakeQuestionData> LoadQuestionAsync(int? examId, Guid recordId, int questionId, string? userId = null, bool requireOwnership = false)
        {
            var questionState = await _examAttemptStateService.LoadQuestionAsync(examId, recordId, questionId, userId, requireOwnership);
            return new ExamTakeQuestionData
            {
                Exists = questionState.Exists,
                UserMatches = questionState.UserMatches,
                ExamId = questionState.ExamId,
                QuestionId = questionId,
                ExamStatus = questionState.ExamStatus,
                Question = questionState.Question,
                SavedAnswerText = questionState.SavedAnswerText
            };
        }

        public async Task<ExamTakeAnswerSaveResult> SaveAnswerAsync(IFormCollection formData)
        {
            if (formData == null)
                return new ExamTakeAnswerSaveResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "No form data was provided." };

            try
            {
                if (!int.TryParse(formData["questionId"], out var questionId))
                    throw new FormatException("Invalid questionId format");

                var examId = ParseNullableInt(formData["examId"]);
                if (!Guid.TryParse(formData["recordId"], out var recordId))
                    throw new FormatException("Invalid recordId format");

                var examStatus = await _recordService.GetExamStatusAsync(recordId, examId);
                if (examStatus != ExamStatus.InProgress)
                {
                    return new ExamTakeAnswerSaveResult
                    {
                        Success = false,
                        StatusCode = StatusCodes.Status409Conflict,
                        ErrorMessage = "Exam attempt is no longer in progress.",
                        ExamId = examId,
                        QuestionId = questionId,
                        RecordId = recordId
                    };
                }

                var answerText = formData["answer"].ToString();
                var source = string.IsNullOrWhiteSpace(formData["source"]) ? "manual" : formData["source"].ToString().Trim();

                var saved = await _participantAnswerService.SaveParticipantAnswerAsync(new ParticipantAnswer
                {
                    AnswerText = answerText,
                    QuestionId = questionId,
                    RecordId = recordId,
                    Timestamp = DateTime.UtcNow
                });

                if (!saved)
                {
                    return new ExamTakeAnswerSaveResult
                    {
                        Success = false,
                        StatusCode = StatusCodes.Status409Conflict,
                        ErrorMessage = "Time limit reached. Your last answer saved before deadline is kept.",
                        ExamId = examId,
                        QuestionId = questionId,
                        RecordId = recordId,
                        AnswerText = answerText,
                        Source = source,
                        SaveRejected = true
                    };
                }

                return new ExamTakeAnswerSaveResult
                {
                    Success = true,
                    StatusCode = StatusCodes.Status200OK,
                    ExamId = examId,
                    QuestionId = questionId,
                    RecordId = recordId,
                    AnswerText = answerText,
                    Source = source
                };
            }
            catch (FormatException ex)
            {
                return new ExamTakeAnswerSaveResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid format: " + ex.Message };
            }
            catch (Exception ex)
            {
                return new ExamTakeAnswerSaveResult { Success = false, StatusCode = StatusCodes.Status500InternalServerError, ErrorMessage = "An error occurred while processing the request: " + ex.Message };
            }
        }

        public async Task<ExamTakeValidationResult> ValidateMissingQuestionsAsync(int? examId, Guid recordId)
        {
            try
            {
                var accessState = await _recordService.GetAttemptAccessStateAsync(recordId, examId);
                if (!accessState.Exists || accessState.ExamId <= 0)
                    return new ExamTakeValidationResult { Success = false, ErrorMessage = "Invalid record." };

                var questions = await _examVersionService.GetQuestionMetadataAsync(accessState.ExamId, accessState.ExamVersionId);
                var answeredQuestions = await _participantAnswerService.GetAnsweredQuestionsAsync(recordId);
                return new ExamTakeValidationResult
                {
                    Success = !questions.Select(q => q.QuestionNumber).Except(answeredQuestions).Any(),
                    MissingQuestionNumbers = questions.Select(q => q.QuestionNumber).Except(answeredQuestions).ToList()
                };
            }
            catch (Exception ex)
            {
                return new ExamTakeValidationResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<ExamTakeSubmitResult> SubmitExamAsync(IFormCollection formData, Func<Guid, string?> buildRedirectUrl)
        {
            if (formData == null)
                return new ExamTakeSubmitResult { Success = false, ErrorMessage = "No form data was provided." };

            try
            {
                var recordId = Guid.Parse(formData["recordId"].ToString().Trim('"'));
                var examId = ParseNullableInt(formData["examId"]);
                var examStatus = await _recordService.GetExamStatusAsync(recordId, examId);
                if (examStatus == ExamStatus.Invalid)
                    return new ExamTakeSubmitResult { Success = false, ErrorMessage = "Invalid record." };

                await _recordService.CompleteRecordAsync(recordId, DateTime.UtcNow);
                return new ExamTakeSubmitResult { Success = true, RedirectUrl = buildRedirectUrl(recordId) };
            }
            catch (Exception ex)
            {
                return new ExamTakeSubmitResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private static int? ParseNullableInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (!int.TryParse(value, out var parsedValue))
                throw new FormatException("Invalid examId format");
            return parsedValue;
        }
    }
}
