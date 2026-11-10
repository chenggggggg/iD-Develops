using System.Globalization;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Tests.Utilities;

public sealed class ExamResultGradeTests
{
    [Theory]
    [InlineData(4.99, "en-US", "Starting")]
    [InlineData(5, "en-US", "Ready")]
    [InlineData(7.5, "nl-NL", "Klaar")]
    [InlineData(0, "nl-NL", "Begin")]
    public void GradeBandsUseHighestMatchingSavedThreshold(double score, string culture, string expected)
    {
        var exam = new Exam
        {
            ResultGradeDisplayMode = ResultGradeDisplayMode.GradeBands,
            GradeBands =
            [
                new() { MinimumScore = 0, LabelPrimary = "Begin", LabelSecondary = "Starting" },
                new() { MinimumScore = 5, LabelPrimary = "Klaar", LabelSecondary = "Ready" }
            ]
        };
        Assert.Equal(("GradeLabel", expected), ExamResultGrade.Resolve(exam, score, 10, 1, 2, CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData("en-US", "Ready for conversation")]
    [InlineData("nl-NL", "Klaar voor een gesprek")]
    public void CustomResultUsesItsSavedTranslation(string culture, string expected)
    {
        var exam = new Exam
        {
            ResultGradeDisplayMode = ResultGradeDisplayMode.CustomText,
            ResultGradeCustomTextPrimary = "Klaar voor een gesprek",
            ResultGradeCustomTextSecondary = "Ready for conversation"
        };
        Assert.Equal(("GradeLabel", expected), ExamResultGrade.Resolve(exam, 2, 3, 1, 2, CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void MissingTranslationFallsBackToTheSavedPrimaryText()
    {
        var exam = new Exam { ResultGradeDisplayMode = ResultGradeDisplayMode.CustomText, ResultGradeCustomTextPrimary = "Klaar" };
        Assert.Equal(("GradeLabel", "Klaar"), ExamResultGrade.Resolve(exam, 2, 3, 1, 2, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void CorrectAnswerModeDoesNotConfuseWeightedPointsWithQuestionCount()
    {
        var exam = new Exam { ResultGradeDisplayMode = ResultGradeDisplayMode.CorrectAnswers };
        Assert.Equal(("CorrectAnswersLabel", "2 / 3"), ExamResultGrade.Resolve(exam, 80, 100, 2, 3, CultureInfo.GetCultureInfo("en-US")));
    }

    [Theory]
    [InlineData(ResultGradeDisplayMode.Score)]
    [InlineData(ResultGradeDisplayMode.GradeBands)]
    [InlineData(ResultGradeDisplayMode.CustomText)]
    public void MissingGradeFallsBackToActualScoreInsteadOfInventingALanguageLevel(ResultGradeDisplayMode mode)
    {
        var exam = new Exam { ResultGradeDisplayMode = mode };
        Assert.Equal(("ScoreLabel", "0 / 0"), ExamResultGrade.Resolve(exam, 0, 0, 0, 0, CultureInfo.GetCultureInfo("en-US")));
    }

    [Theory]
    [InlineData("en-US", "2.5 / 3.5")]
    [InlineData("nl-NL", "2,5 / 3,5")]
    public void ScoreUsesTheReadersNumberFormat(string culture, string expected)
    {
        Assert.Equal(("ScoreLabel", expected), ExamResultGrade.Resolve(new Exam(), 2.5, 3.5, 1, 2, CultureInfo.GetCultureInfo(culture)));
    }
}
