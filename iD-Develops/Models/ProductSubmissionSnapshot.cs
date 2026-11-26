using System.Text.Json;

namespace iD_Develops.Models;

public sealed record ProductSubmissionField(string Key, string Label, bool IsNote = false);

public sealed class ProductSubmissionSnapshot
{
    public const string ProductIdKey = "_registry_product_id";
    public const string FieldsKey = "_form_fields";
    public bool IsValid { get; private init; }
    public bool HasFieldSnapshot { get; private set; }
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public List<ProductSubmissionField> Fields { get; } = [];
    public int? ProductId { get; private set; }
    public string? ProductSlug => Read("product_slug");
    public string? CustomerName => Read(SignupPersonalDetails.FullNameKey) ?? Read("customer_name") ?? Read("full_name") ?? Read("name");
    public string? Read(string key) => Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static ProductSubmissionSnapshot Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new();
            var result = new ProductSubmissionSnapshot { IsValid = true };
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name == FieldsKey)
                {
                    if (property.Value.ValueKind != JsonValueKind.Array) continue;
                    result.HasFieldSnapshot = true;
                    foreach (var field in property.Value.EnumerateArray())
                        if (field.ValueKind == JsonValueKind.Object && field.TryGetProperty("Key", out var key) &&
                            key.ValueKind == JsonValueKind.String && field.TryGetProperty("Label", out var label) &&
                            label.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(key.GetString()) &&
                            !string.IsNullOrWhiteSpace(label.GetString()))
                            result.Fields.Add(new(key.GetString()!, label.GetString()!,
                                field.TryGetProperty("IsNote", out var note) && note.ValueKind == JsonValueKind.True));
                    continue;
                }
                result.Values[property.Name] = Text(property.Value);
            }
            if (int.TryParse(result.Read(ProductIdKey), out var productId) && productId > 0) result.ProductId = productId;
            return result;
        }
        catch (JsonException) { return new(); }
    }

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Array => string.Join("\n", value.EnumerateArray().Select(Text)),
        JsonValueKind.Object => string.Join("\n", value.EnumerateObject().Select(property =>
            $"{CatalogProductSubmissionEmailModel.ReadableLabel(property.Name)}: {Text(property.Value)}")),
        _ => value.ToString()
    };

    public IEnumerable<ProductEmailAnswer> Answers()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (field.IsNote)
            {
                used.Add(field.Key);
                continue;
            }
            if (!IsContactOrMetadata(field.Key) && used.Add(field.Key))
                yield return new() { Label = field.Label, Value = Read(field.Key) ?? string.Empty };
        }
        foreach (var item in Values)
            if (!IsContactOrMetadata(item.Key) && used.Add(item.Key))
                yield return new() { Label = CatalogProductSubmissionEmailModel.ReadableLabel(item.Key), Value = item.Value };
    }

    private static bool IsContactOrMetadata(string key) => CatalogProductSubmissionEmailModel.IsMetadata(key) ||
        key is "customer_name" or "full_name" or "name" or "customer_email" or "email";
}
