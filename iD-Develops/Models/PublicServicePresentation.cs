using System.Text.RegularExpressions;

namespace iD_Develops.Models;

public sealed record PublicServiceLink(string Label, string Url, bool External = false);

public sealed record PublicServiceHero(
    string Eyebrow,
    string Title,
    IReadOnlyList<string> Paragraphs,
    string ImageName,
    PublicServiceLink PrimaryAction,
    PublicServiceLink SecondaryAction,
    string? NoteTitle = null,
    string? NoteDescription = null);

public sealed record PublicServiceOffering(string Title, string Description);

public sealed record PublicServiceContact(
    string Title,
    IReadOnlyList<string> Paragraphs,
    PublicServiceLink PrimaryAction,
    PublicServiceLink? SecondaryAction = null);

public sealed record PublicServiceText(string Opening, string Remainder)
{
    public static PublicServiceText Lead(string text) => Split(text, balanced: false);

    public static PublicServiceText Balanced(string text) => Split(text, balanced: true);

    private static PublicServiceText Split(string text, bool balanced)
    {
        ArgumentNullException.ThrowIfNull(text);
        var boundaries = Regex.Matches(text, """(?<=[.!?])\s+(?=[\p{Lu}“"‘'])""");
        if (boundaries.Count == 0) return new(text, string.Empty);

        var boundary = balanced
            ? boundaries.MinBy(match => Math.Abs(match.Index - text.Length / 2))!
            : boundaries[0];
        return new(text[..boundary.Index], text[(boundary.Index + boundary.Length)..]);
    }
}
