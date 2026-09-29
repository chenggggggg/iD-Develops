using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace iD_Develops.Services
{
    public sealed class ExamTransferService : IExamTransferService
    {
        private const int MaximumQuestionCount = 1000;
        private const int MaximumCorrectAnswersPerQuestion = 100;
        private const int MaximumNameLength = 250;
        private const int MaximumTextLength = 1_000_000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            MaxDepth = 64,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ApplicationDbContext _dbContext;

        public ExamTransferService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ExamTransferExport?> ExportAsync(
            int examId,
            CancellationToken cancellationToken = default)
        {
            if (examId <= 0)
                return null;

            var exam = await _dbContext.Exams
                .AsNoTracking()
                .Include(e => e.Course)
                .Include(e => e.GradeBands)
                .FirstOrDefaultAsync(e => e.Id == examId && !e.IsDeleted, cancellationToken);

            if (exam == null)
                return null;

            var questions = await _dbContext.Questions
                .AsNoTracking()
                .Include(q => q.CorrectAnswers)
                .Include("MultipleChoiceAnswer")
                .Where(q => q.ExamId == examId && !q.IsDeleted)
                .OrderBy(q => q.QuestionNumber)
                .ToListAsync(cancellationToken);

            var package = new ExamTransferPackage
            {
                ExportedAtUtc = DateTime.UtcNow,
                Exam = new ExamTransferDefinition
                {
                    Name = exam.Name,
                    DifficultyValue = exam.DifficultyValue,
                    TimeLimit = exam.TimeLimit,
                    IntroductionPrimaryLanguage = exam.IntroductionPrimaryLanguage,
                    IntroductionSecondaryLanguage = exam.IntroductionSecondaryLanguage,
                    CompletionTextPrimary = exam.CompletionTextPrimary,
                    CompletionTextSecondary = exam.CompletionTextSecondary,
                    ResultGradeDisplayMode = exam.ResultGradeDisplayMode,
                    ResultGradeCustomTextPrimary = exam.ResultGradeCustomTextPrimary,
                    ResultGradeCustomTextSecondary = exam.ResultGradeCustomTextSecondary,
                    MaxAttempts = exam.MaxAttempts,
                    CourseName = exam.Course?.Name,
                    SourcePublicSlug = exam.PublicSlug,
                    GradeBands = exam.GradeBands
                        .OrderByDescending(b => b.MinimumScore)
                        .Select(b => new ExamTransferGradeBand
                        {
                            MinimumScore = b.MinimumScore,
                            LabelPrimary = b.LabelPrimary,
                            LabelSecondary = b.LabelSecondary
                        })
                        .ToList(),
                    Questions = questions.Select(MapQuestionForExport).ToList()
                }
            };

            var content = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
            return new ExamTransferExport(BuildFileName(exam.Name), content);
        }

        public async Task<ExamTransferImportResult> ImportAsync(
            Stream jsonStream,
            string createdByUserId,
            CancellationToken cancellationToken = default)
        {
            if (jsonStream == null || !jsonStream.CanRead)
                return Failure("Preparing import", "Select a readable JSON exam file.");

            if (string.IsNullOrWhiteSpace(createdByUserId))
                return Failure("Preparing import", "The importing user could not be identified.");

            ExamTransferPackage? package;
            try
            {
                package = await JsonSerializer.DeserializeAsync<ExamTransferPackage>(
                    jsonStream,
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException ex)
            {
                var jsonPath = string.IsNullOrWhiteSpace(ex.Path) ? "$" : ex.Path;
                long? lineNumber = ex.LineNumber.HasValue ? ex.LineNumber.Value + 1 : null;
                var bytePosition = ex.BytePositionInLine;
                var location = lineNumber.HasValue
                    ? $"Path {jsonPath}, line {lineNumber.Value}, byte {bytePosition?.ToString() ?? "unknown"}."
                    : $"Path {jsonPath}.";

                return Failure(
                    "Reading JSON",
                    "The selected file is not valid JSON or does not match the exam export structure.",
                    [location, ex.Message]);
            }
            catch (NotSupportedException ex)
            {
                return Failure(
                    "Reading JSON",
                    "The selected file contains a value that this exam importer does not support.",
                    [ex.Message]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ExamTransferImportException(
                    "Reading JSON",
                    "The uploaded file could not be read.",
                    ex);
            }

            if (package == null)
                return Failure("Reading JSON", "The selected file is empty or invalid.");

            try
            {
                ValidatePackage(package);
            }
            catch (ValidationException ex)
            {
                return Failure("Validating exam definition", ex.Message, [ex.Message]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ExamTransferImportException(
                    "Validating exam definition",
                    "The imported exam definition could not be validated.",
                    ex);
            }

            var definition = package.Exam;
            var warnings = new List<string>();
            Course? course = null;

            if (!string.IsNullOrWhiteSpace(definition.CourseName))
            {
                var normalizedCourseName = definition.CourseName.Trim();
                try
                {
                    course = await _dbContext.Courses
                        .FirstOrDefaultAsync(c => c.Name == normalizedCourseName, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new ExamTransferImportException(
                        "Resolving course assignment",
                        "The importer could not check whether the exam's course exists.",
                        ex);
                }

                if (course == null)
                {
                    warnings.Add(
                        $"The course '{normalizedCourseName}' does not exist in this environment, so the imported exam is not assigned to a course.");
                }
            }

            if (!string.IsNullOrWhiteSpace(definition.SourcePublicSlug))
            {
                warnings.Add(
                    "The public slug was not imported. Assign the imported exam to the public route after reviewing and publishing it.");
            }

            Exam exam;
            try
            {
                exam = new Exam
                {
                    Name = definition.Name.Trim(),
                    CreatedByUserId = createdByUserId,
                    DifficultyValue = definition.DifficultyValue,
                    TimeLimit = definition.TimeLimit,
                    IntroductionPrimaryLanguage = definition.IntroductionPrimaryLanguage,
                    IntroductionSecondaryLanguage = definition.IntroductionSecondaryLanguage,
                    CompletionTextPrimary = definition.CompletionTextPrimary,
                    CompletionTextSecondary = definition.CompletionTextSecondary,
                    ResultGradeDisplayMode = definition.ResultGradeDisplayMode,
                    ResultGradeCustomTextPrimary = definition.ResultGradeCustomTextPrimary,
                    ResultGradeCustomTextSecondary = definition.ResultGradeCustomTextSecondary,
                    MaxAttempts = definition.MaxAttempts,
                    Course = course,
                    PublicSlug = null,
                    PublishStatus = ExamPublishStatus.Draft,
                    IsDeleted = false,
                    GradeBands = definition.GradeBands
                        .Select(b => new ExamGradeBand
                        {
                            MinimumScore = b.MinimumScore,
                            LabelPrimary = b.LabelPrimary.Trim(),
                            LabelSecondary = NormalizeOptional(b.LabelSecondary)
                        })
                        .ToList(),
                    Questions = definition.Questions
                        .OrderBy(q => q.QuestionNumber)
                        .Select(MapQuestionForImport)
                        .ToList()
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ExamTransferImportException(
                    "Creating draft exam",
                    "The validated exam data could not be converted into a draft exam.",
                    ex);
            }

            try
            {
                _dbContext.Exams.Add(exam);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // SaveChanges uses its own transaction for the complete exam graph. Detach the
                // failed graph so the scoped DbContext cannot accidentally persist it later.
                foreach (var entry in _dbContext.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
                    entry.State = EntityState.Detached;

                throw new ExamTransferImportException(
                    "Saving draft exam",
                    "The database could not save the imported exam. No exam was created.",
                    ex);
            }

            return new ExamTransferImportResult(true, exam.Id, Warnings: warnings);
        }

        private static ExamTransferQuestion MapQuestionForExport(Question question)
        {
            var multipleChoice = question as MultipleChoiceQuestion;
            return new ExamTransferQuestion
            {
                Type = question switch
                {
                    MultipleChoiceQuestion => "MultipleChoice",
                    TrueOrFalseQuestion => "TrueOrFalse",
                    OpenQuestion => "Open",
                    _ => "Unknown"
                },
                QuestionNumber = question.QuestionNumber,
                MessageBeforeQuestion = question.MessageBeforeQuestion,
                ImageReference = question.ImageReference,
                AudioReference = question.AudioReference,
                Text = question.Text,
                Scenario = question.Scenario,
                Feedback = question.Feedback,
                FunFact = question.FunFact,
                Score = question.Score,
                AnswerA = multipleChoice?.MultipleChoiceAnswer?.AnswerA,
                AnswerB = multipleChoice?.MultipleChoiceAnswer?.AnswerB,
                AnswerC = multipleChoice?.MultipleChoiceAnswer?.AnswerC,
                AnswerD = multipleChoice?.MultipleChoiceAnswer?.AnswerD,
                CorrectAnswers = question.CorrectAnswers
                    .Where(a => !a.IsDeleted)
                    .Select(a => new ExamTransferCorrectAnswer
                    {
                        Text = a.Text,
                        Score = a.Score
                    })
                    .ToList()
            };
        }

        private static Question MapQuestionForImport(ExamTransferQuestion definition)
        {
            Question question = definition.Type switch
            {
                "MultipleChoice" => new MultipleChoiceQuestion
                {
                    MultipleChoiceAnswer = new MultipleChoiceAnswer
                    {
                        AnswerA = NormalizeOptional(definition.AnswerA),
                        AnswerB = NormalizeOptional(definition.AnswerB),
                        AnswerC = NormalizeOptional(definition.AnswerC),
                        AnswerD = NormalizeOptional(definition.AnswerD)
                    }
                },
                "TrueOrFalse" => new TrueOrFalseQuestion(),
                "Open" => new OpenQuestion(),
                _ => throw new ValidationException($"Question {definition.QuestionNumber} has an unsupported type.")
            };

            question.QuestionNumber = definition.QuestionNumber;
            question.MessageBeforeQuestion = NormalizeOptional(definition.MessageBeforeQuestion);
            question.ImageReference = NormalizeOptional(definition.ImageReference);
            question.AudioReference = NormalizeOptional(definition.AudioReference);
            question.Text = definition.Text;
            question.Scenario = NormalizeOptional(definition.Scenario);
            question.Feedback = NormalizeOptional(definition.Feedback);
            question.FunFact = NormalizeOptional(definition.FunFact);
            question.Score = definition.Score;
            question.IsDeleted = false;
            question.CorrectAnswers = definition.CorrectAnswers
                .Select(a => new CorrectAnswer
                {
                    Text = a.Text.Trim(),
                    Score = a.Score,
                    IsDeleted = false
                })
                .ToList();

            return question;
        }

        private static void ValidatePackage(ExamTransferPackage package)
        {
            if (package.FormatVersion != ExamTransferPackage.CurrentFormatVersion)
            {
                throw new ValidationException(
                    $"This exam export uses format version {package.FormatVersion}. Only version {ExamTransferPackage.CurrentFormatVersion} is supported.");
            }

            var exam = package.Exam ?? throw new ValidationException("The file does not contain an exam definition.");
            RequireText(exam.Name, "Exam name", MaximumNameLength);
            RequireText(exam.IntroductionPrimaryLanguage, "Primary introduction", MaximumTextLength, allowEmpty: true);

            if (!Enum.IsDefined(typeof(DifficultyLevel), exam.DifficultyValue))
                throw new ValidationException("The exam difficulty is invalid.");

            if (!Enum.IsDefined(exam.ResultGradeDisplayMode))
                throw new ValidationException("The result display mode is invalid.");

            if (exam.TimeLimit.HasValue && exam.TimeLimit.Value < 1)
                throw new ValidationException("The time limit must be at least one minute.");

            if (exam.MaxAttempts < -1)
                throw new ValidationException("The maximum-attempts setting is invalid.");

            exam.GradeBands ??= new List<ExamTransferGradeBand>();
            exam.Questions ??= new List<ExamTransferQuestion>();

            if (exam.Questions.Count > MaximumQuestionCount)
                throw new ValidationException($"An imported exam can contain at most {MaximumQuestionCount} questions.");

            var nullGradeBandIndex = exam.GradeBands.FindIndex(band => band == null);
            if (nullGradeBandIndex >= 0)
                throw new ValidationException($"Grade band {nullGradeBandIndex + 1} is empty (JSON path $.Exam.GradeBands[{nullGradeBandIndex}]).");

            var nullQuestionIndex = exam.Questions.FindIndex(question => question == null);
            if (nullQuestionIndex >= 0)
                throw new ValidationException($"Question {nullQuestionIndex + 1} is empty (JSON path $.Exam.Questions[{nullQuestionIndex}]).");

            var duplicateNumber = exam.Questions
                .GroupBy(q => q.QuestionNumber)
                .FirstOrDefault(g => g.Key < 1 || g.Count() > 1);

            if (duplicateNumber != null)
            {
                var reason = duplicateNumber.Key < 1
                    ? $"Question number {duplicateNumber.Key} is not positive."
                    : $"Question number {duplicateNumber.Key} occurs {duplicateNumber.Count()} times.";
                throw new ValidationException($"{reason} Every question must have a unique positive question number.");
            }

            for (var bandIndex = 0; bandIndex < exam.GradeBands.Count; bandIndex++)
            {
                var band = exam.GradeBands[bandIndex];
                var bandName = $"Grade band {bandIndex + 1}";
                ValidateFiniteNonNegative(band.MinimumScore, $"{bandName} minimum score");
                RequireText(band.LabelPrimary, $"{bandName} primary label", MaximumNameLength);
                ValidateOptionalText(band.LabelSecondary, $"{bandName} secondary label", MaximumTextLength);
            }

            foreach (var question in exam.Questions)
            {
                if (question.Type is not ("MultipleChoice" or "TrueOrFalse" or "Open"))
                    throw new ValidationException($"Question {question.QuestionNumber} has an unsupported type '{question.Type}'.");

                RequireText(question.Text, $"Question {question.QuestionNumber} text", MaximumTextLength);
                ValidateOptionalScore(question.Score, $"Question {question.QuestionNumber} score");
                ValidateOptionalText(question.MessageBeforeQuestion, $"Question {question.QuestionNumber} message", MaximumTextLength);
                ValidateOptionalText(question.Scenario, $"Question {question.QuestionNumber} scenario", MaximumTextLength);
                ValidateOptionalText(question.Feedback, $"Question {question.QuestionNumber} feedback", MaximumTextLength);
                ValidateOptionalText(question.FunFact, $"Question {question.QuestionNumber} fun fact", MaximumTextLength);
                ValidateOptionalText(question.ImageReference, $"Question {question.QuestionNumber} image reference", MaximumTextLength);
                ValidateOptionalText(question.AudioReference, $"Question {question.QuestionNumber} audio reference", MaximumTextLength);

                question.CorrectAnswers ??= new List<ExamTransferCorrectAnswer>();
                if (question.CorrectAnswers.Count > MaximumCorrectAnswersPerQuestion)
                {
                    throw new ValidationException(
                        $"Question {question.QuestionNumber} has too many correct answers.");
                }

                var nullAnswerIndex = question.CorrectAnswers.FindIndex(answer => answer == null);
                if (nullAnswerIndex >= 0)
                {
                    throw new ValidationException(
                        $"Question {question.QuestionNumber}, correct answer {nullAnswerIndex + 1} is empty.");
                }

                for (var answerIndex = 0; answerIndex < question.CorrectAnswers.Count; answerIndex++)
                {
                    var answer = question.CorrectAnswers[answerIndex];
                    var answerName = $"Question {question.QuestionNumber}, correct answer {answerIndex + 1}";
                    RequireText(answer.Text, answerName, MaximumTextLength);
                    ValidateOptionalScore(answer.Score, $"{answerName} score");
                }
            }
        }

        private static void ValidateOptionalScore(double? value, string fieldName)
        {
            if (value.HasValue)
                ValidateFiniteNonNegative(value.Value, fieldName);
        }

        private static void ValidateFiniteNonNegative(double value, string fieldName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ValidationException($"{fieldName} must be a finite value of zero or greater.");
        }

        private static void RequireText(string? value, string fieldName, int maximumLength, bool allowEmpty = false)
        {
            if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value == null)
                throw new ValidationException($"{fieldName} is required.");

            if (value.Length > maximumLength)
                throw new ValidationException($"{fieldName} is too long.");
        }

        private static void ValidateOptionalText(string? value, string fieldName, int maximumLength)
        {
            if (value?.Length > maximumLength)
                throw new ValidationException($"{fieldName} is too long.");
        }

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static ExamTransferImportResult Failure(
            string stage,
            string message,
            IReadOnlyList<string>? diagnosticDetails = null)
            => new(
                false,
                ErrorMessage: message,
                Warnings: Array.Empty<string>(),
                FailureStage: stage,
                DiagnosticDetails: diagnosticDetails ?? Array.Empty<string>());

        private static string BuildFileName(string examName)
        {
            var safeName = new string(examName
                .Trim()
                .ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray());

            while (safeName.Contains("--", StringComparison.Ordinal))
                safeName = safeName.Replace("--", "-", StringComparison.Ordinal);

            safeName = safeName.Trim('-');
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "exam";

            return $"{safeName}.exam.json";
        }
    }
}
