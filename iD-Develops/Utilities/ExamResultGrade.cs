using System.Globalization;
using iD_Develops.Enums;
using iD_Develops.Models;

namespace iD_Develops.Utilities;

public static class ExamResultGrade
{
    public static (string LabelKey, string Value) Resolve(Exam exam, double score, double maxScore,
        int correctAnswers, int totalQuestions, CultureInfo culture)
    {
        string? Localize(string? primary, string? secondary)
            => culture.TwoLetterISOLanguageName.Equals("nl", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(primary) ? secondary : primary)
                : (string.IsNullOrWhiteSpace(secondary) ? primary : secondary);

        if (exam.ResultGradeDisplayMode == ResultGradeDisplayMode.CorrectAnswers)
            return ("CorrectAnswersLabel", $"{correctAnswers} / {totalQuestions}");

        var label = exam.ResultGradeDisplayMode switch
        {
            ResultGradeDisplayMode.CustomText => Localize(exam.ResultGradeCustomTextPrimary, exam.ResultGradeCustomTextSecondary),
            ResultGradeDisplayMode.GradeBands => exam.GradeBands
                .OrderByDescending(band => band.MinimumScore)
                .Where(band => score >= band.MinimumScore)
                .Select(band => Localize(band.LabelPrimary, band.LabelSecondary))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            _ => null
        };
        return !string.IsNullOrWhiteSpace(label)
            ? ("GradeLabel", label)
            : ("ScoreLabel", $"{score.ToString("0.##", culture)} / {maxScore.ToString("0.##", culture)}");
    }
}
