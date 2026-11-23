using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using iD_Develops.Models;

namespace iD_Develops.Services;

// A question keeps one participant-answer row. Stable field keys preserve individual
// responses across navigation and snapshot versions, including empty/partial answers.
public static class OpenAnswerService
{
    public const int MaximumFields = 20;
    public const int MaximumAnswerLength = 10000;
    private const string SubmissionFormat = "open-answers-v1";

    public static void Validate(List<OpenAnswerField>? fields, double? questionScore)
    {
        if (fields == null || fields.Count > MaximumFields)
            throw new ValidationException($"An open question supports at most {MaximumFields} answer inputs.");
        if (questionScore.HasValue && (!double.IsFinite(questionScore.Value) || questionScore < 0))
            throw new ValidationException("Question points must be a finite number of zero or greater.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var maximum = MaximumScore(questionScore);
        foreach (var field in fields)
        {
            if (field == null || !Regex.IsMatch(field.Key ?? "", "^[a-zA-Z0-9_-]{1,64}$") || !keys.Add(field.Key!))
                throw new ValidationException("Each open answer input needs a unique, stable key.");
            if (field.Label == null || field.Label.Length > 500)
                throw new ValidationException("Answer input labels cannot exceed 500 characters.");
            if (field.AcceptedAnswers == null || field.AcceptedAnswers.Count > 100)
                throw new ValidationException("An answer input supports at most 100 accepted alternatives.");
            foreach (var answer in field.AcceptedAnswers)
            {
                if (answer == null || string.IsNullOrWhiteSpace(answer.Text) || answer.Text.Length > MaximumAnswerLength)
                    throw new ValidationException("Accepted answers must contain text and cannot exceed 10,000 characters.");
                if (answer.Score.HasValue && (!double.IsFinite(answer.Score.Value) || answer.Score < 0 || answer.Score > maximum))
                    throw new ValidationException("Accepted-answer points must be between zero and the question's maximum points.");
            }
        }
    }

    public static bool TryRead(string? text, out Dictionary<string, string> answers)
    {
        answers = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumFields * (MaximumAnswerLength * 6 + 100) + 100)
            return false;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format)
                || format.ValueKind != JsonValueKind.String || format.GetString() != SubmissionFormat
                || !root.TryGetProperty("answers", out var values) || values.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var value in values.EnumerateObject())
            {
                if (answers.Count >= MaximumFields || value.Value.ValueKind != JsonValueKind.String
                    || !answers.TryAdd(value.Name, value.Value.GetString()!))
                    return false;
                if (answers[value.Name].Length > MaximumAnswerLength)
                    return false;
            }
            return answers.Count > 0;
        }
        catch (JsonException) { return false; }
    }

    public static string NormalizeSubmission(OpenQuestion question, string? text)
    {
        if (question.OpenAnswerFields.Count == 0) return text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (!TryRead(text, out var answers) || answers.Count != question.OpenAnswerFields.Count
            || question.OpenAnswerFields.Any(field => !answers.ContainsKey(field.Key)))
            throw new FormatException("The submitted answer inputs do not match this exam question.");
        var ordered = question.OpenAnswerFields.ToDictionary(field => field.Key, field => answers[field.Key].Trim());
        return ordered.Values.All(string.IsNullOrWhiteSpace) ? string.Empty
            : JsonSerializer.Serialize(new { format = SubmissionFormat, answers = ordered });
    }

    public static bool IsComplete(string? text)
        => TryRead(text, out var answers) ? answers.Values.All(value => !string.IsNullOrWhiteSpace(value)) : !string.IsNullOrWhiteSpace(text);

    public static double Evaluate(OpenQuestion question, string? text)
    {
        if (!TryRead(text, out var answers) || answers.Count != question.OpenAnswerFields.Count) return 0;
        var score = MaximumScore(question.Score);
        foreach (var field in question.OpenAnswerFields)
        {
            if (!answers.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value)) return 0;
            var matching = field.AcceptedAnswers.Where(answer => Normalize(answer.Text) == Normalize(value))
                .Select(answer => answer.Score ?? MaximumScore(question.Score)).DefaultIfEmpty(0).Max();
            // All inputs belong to one question: one correct blank must not earn the
            // whole question's points. The lowest matching award determines its score.
            score = Math.Min(score, matching);
        }
        return Math.Clamp(score, 0, MaximumScore(question.Score));
    }

    public static string FormatAnswer(OpenQuestion question, string? text, string emptyFallback)
    {
        TryRead(text, out var answers);
        return string.Join("\n", question.OpenAnswerFields.Select((field, index) =>
            $"{Label(field, index)}: {(answers.TryGetValue(field.Key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : emptyFallback)}"));
    }

    public static string FormatAcceptedAnswers(OpenQuestion question, string emptyFallback)
        => string.Join("\n", question.OpenAnswerFields.Select((field, index) =>
            $"{Label(field, index)}: {(field.AcceptedAnswers.Count == 0 ? emptyFallback : string.Join(" / ", field.AcceptedAnswers.Select(answer => answer.Text + (answer.Score.HasValue ? $" ({answer.Score.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})" : ""))))}"));

    private static string Label(OpenAnswerField field, int index) => string.IsNullOrWhiteSpace(field.Label) ? (index + 1).ToString() : field.Label;
    public static double MaximumScore(double? score) => ExamScoring.MaximumPoints(score);
    private static string Normalize(string text)
    {
        var value = Regex.Replace(text.Trim(), @"\s+", " ").ToUpperInvariant();
        return value.EndsWith('.') ? value[..^1].TrimEnd() : value;
    }
}
