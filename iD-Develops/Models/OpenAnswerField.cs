using System.Text.Json;

namespace iD_Develops.Models;

public sealed class OpenAnswerField
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<OpenAcceptedAnswer> AcceptedAnswers { get; set; } = [];
}

public sealed class OpenAcceptedAnswer
{
    public string Text { get; set; } = string.Empty;
    public double? Score { get; set; }
}

public static class OpenAnswerFieldJson
{
    public static string Serialize(List<OpenAnswerField>? fields) => JsonSerializer.Serialize(fields ?? []);
    public static List<OpenAnswerField> Deserialize(string json) => JsonSerializer.Deserialize<List<OpenAnswerField>>(json) ?? [];
    public static List<OpenAnswerField> Copy(List<OpenAnswerField> fields, bool hideAnswers = false)
        => fields.Select(field => new OpenAnswerField
        {
            Key = field.Key,
            Label = field.Label,
            AcceptedAnswers = hideAnswers ? [] : field.AcceptedAnswers.Select(answer => new OpenAcceptedAnswer
            {
                Text = answer.Text,
                Score = answer.Score
            }).ToList()
        }).ToList();
}
