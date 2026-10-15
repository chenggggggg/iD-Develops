namespace iD_Develops.Utilities;

public static class CatalogContentCulture
{
    public const string Default = "en-US";
    public const string Dutch = "nl-NL";

    public static readonly IReadOnlyList<string> Supported = [Default, Dutch];

    public static string Normalize(string? culture)
        => Supported.FirstOrDefault(candidate =>
               string.Equals(candidate, culture, StringComparison.OrdinalIgnoreCase))
           ?? Default;

    public static bool IsDefault(string? culture)
        => string.Equals(Normalize(culture), Default, StringComparison.Ordinal);
}
