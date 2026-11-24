namespace iD_Develops.Utilities;

public static class PublicLevelTests
{
    // Keep the original A1 designation so existing attempts and links remain valid.
    public const string A1Slug = "level-test";
    public const string A2Slug = "level-test-a2";

    public static string? SlugForLevel(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "a1" => A1Slug,
        "a2" => A2Slug,
        _ => null
    };

    public static string? LevelForSlug(string? slug) => slug?.Trim().ToLowerInvariant() switch
    {
        A1Slug => "a1",
        A2Slug => "a2",
        _ => null
    };

    public static bool IsPublicTest(string? slug) => LevelForSlug(slug) != null;
}
