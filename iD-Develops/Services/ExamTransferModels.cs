using iD_Develops.Enums;

namespace iD_Develops.Services
{
    public sealed class ExamTransferPackage
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public DateTime ExportedAtUtc { get; set; }
        public ExamTransferDefinition Exam { get; set; } = new();
    }

    public sealed class ExamTransferDefinition
    {
        public string Name { get; set; } = string.Empty;
        public int DifficultyValue { get; set; }
        public int? TimeLimit { get; set; }
        public string IntroductionPrimaryLanguage { get; set; } = string.Empty;
        public string? IntroductionSecondaryLanguage { get; set; }
        public string? CompletionTextPrimary { get; set; }
        public string? CompletionTextSecondary { get; set; }
        public ResultGradeDisplayMode ResultGradeDisplayMode { get; set; } = ResultGradeDisplayMode.Score;
        public string? ResultGradeCustomTextPrimary { get; set; }
        public string? ResultGradeCustomTextSecondary { get; set; }
        public int MaxAttempts { get; set; } = 1;
        public string? CourseName { get; set; }
        public string? SourcePublicSlug { get; set; }
        public List<ExamTransferGradeBand> GradeBands { get; set; } = new();
        public List<ExamTransferQuestion> Questions { get; set; } = new();
    }

    public sealed class ExamTransferGradeBand
    {
        public double MinimumScore { get; set; }
        public string LabelPrimary { get; set; } = string.Empty;
        public string? LabelSecondary { get; set; }
    }

    public sealed class ExamTransferQuestion
    {
        public string Type { get; set; } = string.Empty;
        public int QuestionNumber { get; set; }
        public string? MessageBeforeQuestion { get; set; }
        public string? ImageReference { get; set; }
        public string? AudioReference { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Scenario { get; set; }
        public string? Feedback { get; set; }
        public string? FunFact { get; set; }
        public double? Score { get; set; }
        public string? AnswerA { get; set; }
        public string? AnswerB { get; set; }
        public string? AnswerC { get; set; }
        public string? AnswerD { get; set; }
        public List<ExamTransferCorrectAnswer> CorrectAnswers { get; set; } = new();
    }

    public sealed class ExamTransferCorrectAnswer
    {
        public string Text { get; set; } = string.Empty;
        public double? Score { get; set; }
    }
}
