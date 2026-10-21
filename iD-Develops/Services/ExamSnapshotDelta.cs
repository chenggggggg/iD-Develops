using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using iD_Develops.Models;

namespace iD_Develops.Services;

// Property paths and explicit removal flags distinguish clearing a nullable
// value from inheriting it. Collections use stable keys, never array indexes.
internal static class ExamSnapshotDelta
{
    internal sealed record Change(string[] Path, bool Remove, JsonNode? Value);

    public static JsonObject Encode(ExamVersion snapshot)
    {
        var document = JsonSerializer.SerializeToNode(new
        {
            snapshot.Name,
            snapshot.DifficultyValue,
            snapshot.TimeLimitSeconds,
            snapshot.IntroductionPrimaryLanguage,
            snapshot.IntroductionSecondaryLanguage,
            snapshot.CompletionTextPrimary,
            snapshot.CompletionTextSecondary,
            snapshot.ResultGradeDisplayMode,
            snapshot.ResultGradeCustomTextPrimary,
            snapshot.ResultGradeCustomTextSecondary,
            snapshot.MaxAttempts,
            snapshot.PublicSlug,
            snapshot.CourseName
        })!.AsObject();
        var questions = new JsonObject();
        foreach (var question in snapshot.Questions.OrderBy(q => q.SourceQuestionId))
        {
            var node = JsonSerializer.SerializeToNode(new
            {
                question.SourceQuestionId,
                question.QuestionType,
                question.QuestionNumber,
                question.MessageBeforeQuestion,
                question.ImageReference,
                question.AudioReference,
                question.Text,
                question.Scenario,
                question.Feedback,
                question.FunFact,
                question.Score,
                question.AnswerA,
                question.AnswerB,
                question.AnswerC,
                question.AnswerD
            })!.AsObject();
            // Answer payloads are content addressed: editing/removing one answer
            // leaves every unchanged answer inherited, including legacy roots.
            node["CorrectAnswers"] = ContentCollection(question.CorrectAnswers.Select(answer =>
                JsonSerializer.SerializeToNode(new { answer.Text, answer.Score })!));
            node["CorrectAnswerOrder"] = new JsonArray(node["CorrectAnswers"]!.AsObject()
                .Select(answer => (JsonNode?)JsonValue.Create(answer.Key)).ToArray());
            questions.Add(question.SourceQuestionId.ToString(System.Globalization.CultureInfo.InvariantCulture), node);
        }
        document["Questions"] = questions;
        document["GradeBands"] = ContentCollection(snapshot.GradeBands.Select(band =>
            JsonSerializer.SerializeToNode(new { band.MinimumScore, band.LabelPrimary, band.LabelSecondary })!));
        return document;
    }

    public static ExamVersion Decode(JsonObject document)
    {
        var metadata = document.DeepClone().AsObject();
        metadata.Remove("Questions");
        metadata.Remove("GradeBands");
        var snapshot = metadata.Deserialize<ExamVersion>()!;
        foreach (var entry in document["Questions"]!.AsObject())
        {
            var node = entry.Value!.DeepClone().AsObject();
            var answers = node["CorrectAnswers"]!.AsObject();
            var answerOrder = node["CorrectAnswerOrder"]!.AsArray().Select(key => key!.GetValue<string>()).ToList();
            node.Remove("CorrectAnswers");
            node.Remove("CorrectAnswerOrder");
            var question = node.Deserialize<ExamVersionQuestion>()!;
            question.CorrectAnswers = answerOrder.Select((key, index) =>
            {
                var value = answers[key]!.Deserialize<ExamVersionCorrectAnswer>()!;
                value.Id = index + 1;
                return value;
            }).ToList();
            snapshot.Questions.Add(question);
        }
        snapshot.GradeBands = document["GradeBands"]!.AsObject()
            .Select(band => band.Value!.Deserialize<ExamVersionGradeBand>()!).ToList();
        return snapshot;
    }

    public static List<Change> Compare(JsonObject? previous, JsonObject current)
    {
        var changes = new List<Change>();
        Visit(previous, current, []);
        return changes;

        void Visit(JsonNode? before, JsonNode? after, string[] path)
        {
            if (JsonNode.DeepEquals(before, after))
                return;
            if (before is JsonObject oldObject && after is JsonObject newObject)
            {
                foreach (var key in oldObject.Select(pair => pair.Key).Union(newObject.Select(pair => pair.Key)).Order(StringComparer.Ordinal))
                {
                    var childPath = path.Append(key).ToArray();
                    if (!newObject.ContainsKey(key))
                        changes.Add(new Change(childPath, true, null));
                    else if (!oldObject.ContainsKey(key))
                        changes.Add(new Change(childPath, false, newObject[key]?.DeepClone()));
                    else
                        Visit(oldObject[key], newObject[key], childPath);
                }
            }
            else
                changes.Add(new Change(path, false, after?.DeepClone()));
        }
    }

    public static JsonObject Apply(JsonObject? document, string json)
    {
        var result = document?.DeepClone().AsObject();
        foreach (var change in JsonSerializer.Deserialize<List<Change>>(json)!)
        {
            if (change.Path.Length == 0)
            {
                result = change.Value!.DeepClone().AsObject();
                continue;
            }
            var parent = result ?? throw new InvalidOperationException("Exam version has no root snapshot.");
            foreach (var part in change.Path[..^1])
                parent = parent[part]!.AsObject();
            if (change.Remove)
                parent.Remove(change.Path[^1]);
            else
                parent[change.Path[^1]] = change.Value?.DeepClone();
        }
        return result ?? throw new InvalidOperationException("Exam version has no snapshot.");
    }

    private static JsonObject ContentCollection(IEnumerable<JsonNode> values)
    {
        var result = new JsonObject();
        foreach (var value in values)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToJsonString())));
            var key = hash;
            for (var occurrence = 1; result.ContainsKey(key); occurrence++)
                key = $"{hash}_{occurrence}";
            result.Add(key, value);
        }
        return result;
    }
}
