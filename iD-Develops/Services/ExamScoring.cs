using iD_Develops.Models;

namespace iD_Develops.Services;

public static class ExamScoring
{
    public static double MaximumPoints(double? score)
        => score.HasValue && double.IsFinite(score.Value) && score.Value > 0 ? score.Value : 1d;

    public static double TotalMaximum(IEnumerable<Question> questions)
        => questions.Sum(MaximumPoints);

    public static double MaximumPoints(Question question)
        => question is OpenQuestion open ? OpenAnswerService.MaximumScore(open) : MaximumPoints(question.Score);

    public static double MaximumPoints(ExamVersionQuestion question)
        => question.QuestionType == "Open" ? OpenAnswerService.MaximumScore(new OpenQuestion
        { Score = question.Score, OpenAnswerFields = question.OpenAnswerFields }) : MaximumPoints(question.Score);

    public static double LegacyAward(Question question, CorrectAnswer answer)
    {
        var maximum = MaximumPoints(question.Score);
        if (question.Score.HasValue && double.IsFinite(question.Score.Value) && question.Score > 0)
            return maximum;
        var award = answer.Score.HasValue && double.IsFinite(answer.Score.Value) && answer.Score > 0
            ? answer.Score.Value : 1d;
        return Math.Clamp(award, 0, maximum);
    }
}
