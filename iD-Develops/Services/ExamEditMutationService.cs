using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Utilities;
using System.Net;
using System.Text.RegularExpressions;

namespace iD_Develops.Services
{
    public sealed class ExamEditMutationService : IExamEditMutationService
    {
        private readonly IExamService _examService;
        private readonly IQuestionService _questionService;
        private readonly IExamVersionService _examVersionService;
        private readonly IPresignedUrlService _presignedUrlService;

        public ExamEditMutationService(
            IExamService examService,
            IQuestionService questionService,
            IExamVersionService examVersionService,
            IPresignedUrlService presignedUrlService)
        {
            _examService = examService;
            _questionService = questionService;
            _examVersionService = examVersionService;
            _presignedUrlService = presignedUrlService;
        }

        public async Task<SaveQuestionResult> SaveQuestionAsync(SaveQuestionCommand command, string? userId, bool isAdmin)
        {
            if (command.ExamId <= 0)
                return new SaveQuestionResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid examId." };
            if (command.QuestionNumber <= 0)
                return new SaveQuestionResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid questionNumber." };
            if (string.IsNullOrWhiteSpace(command.Kind))
                return new SaveQuestionResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Missing kind." };
            if (!await CanManageExamAsync(command.ExamId, userId, isAdmin))
                return new SaveQuestionResult { Success = false, StatusCode = StatusCodes.Status403Forbidden, ErrorMessage = "You do not have permission to manage this exam." };

            if (command.QuestionId <= 0)
            {
                var existingQuestion = await _questionService.GetQuestionByNumberForUpdateAsync(
                    command.ExamId,
                    command.QuestionNumber);

                if (existingQuestion != null)
                {
                    if (!MatchesQuestionKind(existingQuestion, command.Kind))
                    {
                        return new SaveQuestionResult
                        {
                            Success = false,
                            StatusCode = StatusCodes.Status409Conflict,
                            ErrorMessage = $"Question number {command.QuestionNumber} already exists."
                        };
                    }

                    await ApplyPatchAsync(existingQuestion, command);
                    await _questionService.UpdateQuestionAsync(existingQuestion);

                    return new SaveQuestionResult
                    {
                        Success = true,
                        StatusCode = StatusCodes.Status200OK,
                        QuestionId = existingQuestion.Id,
                        QuestionNumber = existingQuestion.QuestionNumber
                    };
                }

                Question newQuestion = command.Kind switch
                {
                    "Open" => new OpenQuestion { Text = string.Empty },
                    "MultipleChoice" => new MultipleChoiceQuestion { Text = string.Empty, MultipleChoiceAnswer = new MultipleChoiceAnswer() },
                    "TrueOrFalse" => new TrueOrFalseQuestion { Text = string.Empty },
                    _ => throw new InvalidOperationException("Unknown question type")
                };

                newQuestion.QuestionNumber = command.QuestionNumber;
                await _questionService.AddQuestionAsync(command.ExamId, newQuestion);
                await ApplyPatchAsync(newQuestion, command);
                await _questionService.UpdateQuestionAsync(newQuestion);

                return new SaveQuestionResult
                {
                    Success = true,
                    StatusCode = StatusCodes.Status200OK,
                    QuestionId = newQuestion.Id,
                    QuestionNumber = newQuestion.QuestionNumber
                };
            }

            var question = await _questionService.GetQuestionForExamForUpdateAsync(command.ExamId, command.QuestionId);
            if (question == null)
            {
                return new SaveQuestionResult
                {
                    Success = false,
                    StatusCode = StatusCodes.Status404NotFound,
                    ErrorMessage = "Question not found for this exam."
                };
            }

            await ApplyPatchAsync(question, command);
            await _questionService.UpdateQuestionAsync(question);

            return new SaveQuestionResult
            {
                Success = true,
                StatusCode = StatusCodes.Status200OK,
                QuestionId = question.Id,
                QuestionNumber = question.QuestionNumber
            };
        }

        public async Task<SettingsMutationResult> SaveSettingsAsync(int examId, CreateExamInputModel settings, string? userId, bool isAdmin)
        {
            if (examId <= 0)
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status404NotFound };
            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status403Forbidden, ErrorMessage = "You do not have permission to manage this exam." };

            var exam = await _examService.GetExamForSettingsUpdateAsync(examId);
            if (exam == null)
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status404NotFound };

            exam.Name = settings.Name?.Trim();
            exam.TimeLimit = settings.TimeLimit;
            exam.DifficultyValue = settings.DifficultyValue ?? 0;
            exam.MaxAttempts = NormalizeMaxAttempts(settings.MaxAttempts);
            exam.IntroductionPrimaryLanguage = settings.IntroductionPrimaryLanguage ?? string.Empty;
            exam.CompletionTextPrimary = settings.CompletionTextPrimary;
            exam.IntroductionSecondaryLanguage = settings.IntroductionSecondaryLanguage;
            exam.CompletionTextSecondary = settings.CompletionTextSecondary;
            exam.ResultGradeDisplayMode = settings.ResultGradeDisplayMode;
            exam.ResultGradeCustomTextPrimary = settings.ResultGradeCustomTextPrimary;
            exam.ResultGradeCustomTextSecondary = settings.ResultGradeCustomTextSecondary;
            exam.GradeBands.Clear();

            foreach (var band in NormalizeGradeBands(settings.ResultGradeBands))
            {
                exam.GradeBands.Add(new ExamGradeBand
                {
                    MinimumScore = band.MinimumScore,
                    LabelPrimary = band.LabelPrimary,
                    LabelSecondary = band.LabelSecondary
                });
            }

            if (isAdmin)
                exam.PublicSlug = NormalizePublicSlug(settings.PublicSlug);

            await _examService.SaveChangesAsync();
            return new SettingsMutationResult { Success = true, StatusCode = StatusCodes.Status200OK };
        }

        public async Task<PresignedUploadResult> CreateUploadAsync(int examId, int questionId, string fileName, string? contentType, string? userId, bool isAdmin)
        {
            if (examId <= 0)
                return new PresignedUploadResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid examId." };
            if (questionId <= 0)
                return new PresignedUploadResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid questionId." };
            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return new PresignedUploadResult { Success = false, StatusCode = StatusCodes.Status403Forbidden, ErrorMessage = "You do not have permission to manage this exam." };

            var question = await _questionService.GetQuestionForExamForUpdateAsync(examId, questionId);
            if (question == null)
                return new PresignedUploadResult { Success = false, StatusCode = StatusCodes.Status404NotFound, ErrorMessage = "Question not found for this exam." };

            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            var isAllowedExtension = ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".mp3" or ".m4a" or ".aac" or ".ogg" or ".wav" or ".webm";
            if (!isAllowedExtension)
                return new PresignedUploadResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Unsupported file type." };

            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = ext switch
                {
                    ".mp3" => "audio/mpeg",
                    ".m4a" => "audio/mp4",
                    ".aac" => "audio/aac",
                    ".ogg" => "audio/ogg",
                    ".wav" => "audio/wav",
                    ".webm" => "audio/webm",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    ".gif" => "image/gif",
                    _ => null
                };
            }

            if (string.IsNullOrWhiteSpace(contentType))
                return new PresignedUploadResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Unsupported content type." };

            var kind = ext is ".mp3" or ".m4a" or ".aac" or ".wav" or ".ogg" or ".webm" ? "audio" : "image";
            var objectKey = BuildQuestionObjectKey(examId, questionId, kind);

            try
            {
                return new PresignedUploadResult
                {
                    Success = true,
                    StatusCode = StatusCodes.Status200OK,
                    ObjectKey = objectKey,
                    PutUrl = await _presignedUrlService.GeneratePresignedUrl(objectKey, contentType)
                };
            }
            catch
            {
                return new PresignedUploadResult
                {
                    Success = false,
                    StatusCode = StatusCodes.Status200OK,
                    ErrorMessage = "Upload initialization failed.",
                    ErrorCode = "PRESIGN_FAILED"
                };
            }
        }

        public async Task<SettingsMutationResult> DeleteQuestionFileAsync(int examId, int questionId, string kind, string? userId, bool isAdmin)
        {
            if (examId <= 0)
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid examId." };
            if (questionId <= 0)
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid questionId." };
            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status403Forbidden, ErrorMessage = "You do not have permission to manage this exam." };

            kind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind != "image" && kind != "audio")
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid kind." };

            var question = await _questionService.GetQuestionForExamForUpdateAsync(examId, questionId);
            if (question == null)
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status404NotFound, ErrorMessage = "Question not found for this exam." };

            try
            {
                foreach (var objectKey in BuildQuestionDeleteKeys(examId, question, kind))
                {
                    await _presignedUrlService.DeleteObjectAsync(objectKey);
                }
            }
            catch (InvalidOperationException)
            {
            }

            return new SettingsMutationResult { Success = true, StatusCode = StatusCodes.Status200OK };
        }

        public async Task<DeleteQuestionResult> DeleteQuestionAsync(int examId, int questionId, string? userId, bool isAdmin)
        {
            if (examId <= 0)
                return new DeleteQuestionResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid examId." };
            if (questionId <= 0)
                return new DeleteQuestionResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid questionId." };
            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return new DeleteQuestionResult { Success = false, StatusCode = StatusCodes.Status403Forbidden, ErrorMessage = "You do not have permission to manage this exam." };

            var question = await _questionService.GetQuestionForExamForUpdateAsync(examId, questionId);
            if (question == null)
                return new DeleteQuestionResult { Success = false, StatusCode = StatusCodes.Status404NotFound, ErrorMessage = "Question not found for this exam." };

            try
            {
                foreach (var objectKey in BuildQuestionDeleteKeys(examId, question, "image")
                    .Concat(BuildQuestionDeleteKeys(examId, question, "audio"))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    await _presignedUrlService.DeleteObjectAsync(objectKey);
                }
            }
            catch
            {
            }

            await _questionService.DeleteQuestionFromExamAsync(examId, questionId);
            var questionsMetadata = await _questionService.GetQuestionMetadataByExamIdAsync(examId);

            return new DeleteQuestionResult
            {
                Success = true,
                StatusCode = StatusCodes.Status200OK,
                NextQuestionId = questionsMetadata.Any() ? questionsMetadata[0].QuestionId : 0
            };
        }

        public async Task<PublishExamResult> PublishExamAsync(int examId, bool publishWithWarnings, string? userId, bool isAdmin)
        {
            if (examId <= 0)
                return new PublishExamResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid examId." };
            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return new PublishExamResult { Success = false, StatusCode = StatusCodes.Status403Forbidden, ErrorMessage = "You do not have permission to manage this exam." };

            var questions = await _questionService.GetQuestionsForExamForEditAsync(examId);
            var errors = new List<object>();
            var warnings = new List<object>();

            if (!questions.Any())
            {
                errors.Add(new { code = "NO_QUESTIONS", message = "Add at least one question before publishing." });
            }

            foreach (var duplicateNumber in questions
                .GroupBy(question => question.QuestionNumber)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(number => number))
            {
                errors.Add(new
                {
                    code = "DUPLICATE_QUESTION_NUMBER",
                    questionNumber = duplicateNumber,
                    message = $"Question number {duplicateNumber} exists more than once. Delete the duplicate before publishing."
                });
            }

            var questionStates = new List<(int QuestionId, int QuestionNumber, bool HasCorrectAnswer)>();

            foreach (var question in questions)
            {
                var normalizedQuestionText = NormalizeRichText(question.Text);
                if (string.IsNullOrWhiteSpace(normalizedQuestionText))
                {
                    errors.Add(new
                    {
                        code = "QUESTION_TEXT_REQUIRED",
                        questionId = question.Id,
                        questionNumber = question.QuestionNumber,
                        message = $"Question {question.QuestionNumber}: question text is required."
                    });
                }

                var hasCorrectAnswer = question.CorrectAnswers.Any(a => !string.IsNullOrWhiteSpace(a.Text));
                questionStates.Add((question.Id, question.QuestionNumber, hasCorrectAnswer));

                if (question is MultipleChoiceQuestion mcq)
                {
                    var answerA = string.IsNullOrWhiteSpace(mcq.MultipleChoiceAnswer?.AnswerA) ? null : mcq.MultipleChoiceAnswer.AnswerA.Trim();
                    var answerB = string.IsNullOrWhiteSpace(mcq.MultipleChoiceAnswer?.AnswerB) ? null : mcq.MultipleChoiceAnswer.AnswerB.Trim();
                    var answerC = string.IsNullOrWhiteSpace(mcq.MultipleChoiceAnswer?.AnswerC) ? null : mcq.MultipleChoiceAnswer.AnswerC.Trim();
                    var answerD = string.IsNullOrWhiteSpace(mcq.MultipleChoiceAnswer?.AnswerD) ? null : mcq.MultipleChoiceAnswer.AnswerD.Trim();

                    var filledCount = (string.IsNullOrWhiteSpace(answerA) ? 0 : 1)
                                    + (string.IsNullOrWhiteSpace(answerB) ? 0 : 1)
                                    + (string.IsNullOrWhiteSpace(answerC) ? 0 : 1)
                                    + (string.IsNullOrWhiteSpace(answerD) ? 0 : 1);

                    if (filledCount > 0 && filledCount < 4)
                    {
                        var missing = new List<string>();
                        if (string.IsNullOrWhiteSpace(answerA)) missing.Add("A");
                        if (string.IsNullOrWhiteSpace(answerB)) missing.Add("B");
                        if (string.IsNullOrWhiteSpace(answerC)) missing.Add("C");
                        if (string.IsNullOrWhiteSpace(answerD)) missing.Add("D");

                        errors.Add(new
                        {
                            code = "MCQ_OPTIONS_MIXED",
                            questionId = question.Id,
                            questionNumber = question.QuestionNumber,
                            missingOptions = missing,
                            message = $"Question {question.QuestionNumber}: when any MCQ option text is provided, all options A-D must have text (or leave all empty to use default A/B/C/D labels). Missing: {string.Join(", ", missing)}."
                        });
                    }
                }
            }

            var hasAnyCorrectAnswers = questionStates.Any(q => q.HasCorrectAnswer);
            var hasAnyMissingCorrectAnswers = questionStates.Any(q => !q.HasCorrectAnswer);
            if (hasAnyCorrectAnswers && hasAnyMissingCorrectAnswers)
            {
                var missingQuestionNumbers = questionStates.Where(q => !q.HasCorrectAnswer).Select(q => q.QuestionNumber).OrderBy(n => n).ToList();
                warnings.Add(new
                {
                    code = "MISSING_CORRECT_ANSWERS",
                    questionNumbers = missingQuestionNumbers,
                    message = $"Some questions are missing correct answers ({string.Join(", ", missingQuestionNumbers)}). Automatic scoring may be incomplete."
                });
            }

            if (errors.Any())
            {
                return new PublishExamResult
                {
                    Success = true,
                    StatusCode = StatusCodes.Status200OK,
                    Published = false,
                    CanPublish = false,
                    Errors = errors,
                    Warnings = warnings
                };
            }

            if (warnings.Any() && !publishWithWarnings)
            {
                return new PublishExamResult
                {
                    Success = true,
                    StatusCode = StatusCodes.Status200OK,
                    Published = false,
                    CanPublish = true,
                    RequiresWarningConfirmation = true,
                    Warnings = warnings
                };
            }

            var exam = await _examService.GetExamForSettingsUpdateAsync(examId);
            if (exam == null)
                return new PublishExamResult { Success = false, StatusCode = StatusCodes.Status404NotFound, ErrorMessage = "Exam not found." };

            var publishResult = await _examVersionService.PublishVersionAsync(exam, questions);
            if (!publishResult.Success)
                return new PublishExamResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = publishResult.ErrorMessage ?? "Failed to publish exam version." };

            return new PublishExamResult
            {
                Success = true,
                StatusCode = StatusCodes.Status200OK,
                Published = true,
                Message = "Exam published successfully."
            };
        }

        public async Task<SettingsMutationResult> UnpublishExamAsync(int examId, string? userId, bool isAdmin)
        {
            if (examId <= 0)
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status400BadRequest, ErrorMessage = "Invalid examId." };
            if (!await CanManageExamAsync(examId, userId, isAdmin))
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status403Forbidden, ErrorMessage = "You do not have permission to manage this exam." };

            var exam = await _examService.GetExamForSettingsUpdateAsync(examId);
            if (exam == null)
                return new SettingsMutationResult { Success = false, StatusCode = StatusCodes.Status404NotFound, ErrorMessage = "Exam not found." };

            exam.PublishStatus = ExamPublishStatus.Draft;
            await _examService.SaveChangesAsync();
            return new SettingsMutationResult { Success = true, StatusCode = StatusCodes.Status200OK };
        }

        private async Task ApplyPatchAsync(Question question, SaveQuestionCommand command)
        {
            await _questionService.ApplyAutosavePatchAsync(
                question,
                command.Text,
                command.MessageBeforeQuestion,
                command.Scenario,
                command.Feedback,
                command.FunFact,
                command.Score,
                command.OpenCorrectAnswerText,
                command.MultipleChoiceAnswerA,
                command.MultipleChoiceAnswerB,
                command.MultipleChoiceAnswerC,
                command.MultipleChoiceAnswerD,
                command.MultipleChoiceCorrect,
                command.TrueOrFalseCorrect,
                command.ImageReference,
                command.AudioReference);
        }

        private static bool MatchesQuestionKind(Question question, string kind)
            => (question, kind) switch
            {
                (OpenQuestion, "Open") => true,
                (MultipleChoiceQuestion, "MultipleChoice") => true,
                (TrueOrFalseQuestion, "TrueOrFalse") => true,
                _ => false
            };

        private static string? NormalizePublicSlug(string? input)
            => string.IsNullOrWhiteSpace(input) ? null : input.Trim().ToLowerInvariant();

        private static string BuildQuestionObjectKey(int examId, int questionId, string kind)
            => $"exams/questions/{examId}/{questionId}/{kind}";

        private static IEnumerable<string> BuildQuestionDeleteKeys(int examId, Question question, string kind)
        {
            var storedReference = kind == "image" ? question.ImageReference : question.AudioReference;

            if (!string.IsNullOrWhiteSpace(storedReference) &&
                !storedReference.Contains("://", StringComparison.Ordinal))
            {
                yield return storedReference.Trim().TrimStart('/');
            }

            yield return BuildQuestionObjectKey(examId, question.Id, kind);
            yield return $"questions/{question.Id}/{kind}";
        }

        private static int NormalizeMaxAttempts(int? maxAttempts)
            => maxAttempts is >= 1 ? maxAttempts.Value : -1;

        private static List<(double MinimumScore, string LabelPrimary, string? LabelSecondary)> NormalizeGradeBands(IEnumerable<ResultGradeBandInputModel>? bands)
        {
            var orderedBands = (bands ?? Enumerable.Empty<ResultGradeBandInputModel>())
                .Where(b => b.MaximumScore.HasValue && !string.IsNullOrWhiteSpace(b.LabelPrimary))
                .Select(b => (
                    MaximumScore: Math.Round(Math.Max(0, b.MaximumScore!.Value), 1, MidpointRounding.AwayFromZero),
                    LabelPrimary: b.LabelPrimary!.Trim()))
                .OrderBy(b => b.MaximumScore)
                .ToList();

            var normalized = new List<(double MinimumScore, string LabelPrimary, string? LabelSecondary)>(orderedBands.Count);
            var currentMinimum = 0d;

            foreach (var band in orderedBands)
            {
                normalized.Add((
                    MinimumScore: currentMinimum,
                    LabelPrimary: band.LabelPrimary,
                    LabelSecondary: (string?)null));

                currentMinimum = Math.Round(band.MaximumScore + 0.1d, 1, MidpointRounding.AwayFromZero);
            }

            return normalized
                .OrderByDescending(b => b.MinimumScore)
                .ToList();
        }

        private async Task<bool> CanManageExamAsync(int examId, string? userId, bool isAdmin)
        {
            if (isAdmin)
                return true;

            if (string.IsNullOrWhiteSpace(userId))
                return false;

            return await _examService.CanTeacherManageExamAsync(examId, userId);
        }

        private static string NormalizeRichText(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var decoded = WebUtility.HtmlDecode(input);
            var withoutTags = Regex.Replace(decoded, "<[^>]+>", " ");
            return withoutTags.Replace("&nbsp;", " ").Trim();
        }
    }
}
