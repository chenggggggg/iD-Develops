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
                if (answer.Score.HasValue && (!double.IsFinite(answer.Score.Value) || answer.Score < 0))
                    throw new ValidationException("Accepted-answer points must be a finite number of zero or greater.");
            }
        }
        if (fields.Count > 0)
        {
            var maximum = MaximumScore(new OpenQuestion { Score = questionScore, OpenAnswerFields = fields });
            if (!double.IsFinite(maximum) || maximum <= 0)
                throw new ValidationException("The answer inputs must have a finite total greater than zero.");
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
        double score = 0;
        foreach (var field in question.OpenAnswerFields)
        {
            if (!answers.TryGetValue(field.Key, out var value)) return 0;
            if (string.IsNullOrWhiteSpace(value)) continue;
            var matching = field.AcceptedAnswers.Where(answer => Normalize(answer.Text) == Normalize(value))
                .Select(answer => ResolveAward(question, answer)).DefaultIfEmpty(0).Max();
            // Alternatives compete within one input; separate inputs contribute separately.
            score += matching;
        }
        return Math.Clamp(score, 0, MaximumScore(question));
    }

    // Old format-3 keys used a blank award for the whole question's "Full" value.
    // Divide that default between inputs so existing om/te keys retain their one-point total.
    // The editor writes explicit awards; subsequent changes never depend on this fallback.
    public static double ResolveAward(OpenQuestion question, OpenAcceptedAnswer answer)
        => answer.Score.HasValue && double.IsFinite(answer.Score.Value)
            ? Math.Max(0, answer.Score.Value)
            : MaximumScore(question.Score) / Math.Max(1, question.OpenAnswerFields.Count);

    public static double InputMaximum(OpenQuestion question, OpenAnswerField field)
        => field.AcceptedAnswers.Select(answer => ResolveAward(question, answer)).DefaultIfEmpty(1).Max();

    public static double MaximumScore(OpenQuestion question)
        => question.OpenAnswerFields.Count == 0 ? MaximumScore(question.Score)
            : question.OpenAnswerFields.Sum(field => InputMaximum(question, field));

    public static List<OpenAnswerField> ExplicitFields(OpenQuestion question)
    {
        var fields = OpenAnswerFieldJson.Copy(question.OpenAnswerFields);
        foreach (var field in fields)
            foreach (var answer in field.AcceptedAnswers)
                answer.Score = ResolveAward(question, answer);
        return fields;
    }

    public static string FormatAnswer(OpenQuestion question, string? text, string emptyFallback)
    {
        TryRead(text, out var answers);
        return string.Join("\n", question.OpenAnswerFields.Select((field, index) =>
            $"{Label(field, index)}: {(answers.TryGetValue(field.Key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : emptyFallback)}"));
    }

    public static string FormatAcceptedAnswers(OpenQuestion question, string emptyFallback)
        => string.Join("\n", question.OpenAnswerFields.Select((field, index) =>
        {
            var alternatives = field.AcceptedAnswers.Select(answer =>
                $"{answer.Text} ({ResolveAward(question, answer).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})");
            return $"{Label(field, index)}: {(field.AcceptedAnswers.Count == 0 ? emptyFallback : string.Join(" / ", alternatives))}";
        }));

    private static string Label(OpenAnswerField field, int index) => string.IsNullOrWhiteSpace(field.Label) ? (index + 1).ToString() : field.Label;
    public static double MaximumScore(double? score) => ExamScoring.MaximumPoints(score);
    private static string Normalize(string text)
    {
        var value = Regex.Replace(text.Trim(), @"\s+", " ").ToUpperInvariant();
        return value.EndsWith('.') ? value[..^1].TrimEnd() : value;
    }
}
