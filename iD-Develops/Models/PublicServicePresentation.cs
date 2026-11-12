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
