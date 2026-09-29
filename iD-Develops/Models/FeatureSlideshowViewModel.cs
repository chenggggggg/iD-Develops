namespace iD_Develops.Models;

public sealed record FeatureSlideshowViewModel(
    string Eyebrow,
    string Title,
    IReadOnlyList<FeatureSlideshowItem> Features,
    string? Id = null,
    int IntervalMilliseconds = 8000);

public sealed record FeatureSlideshowItem(
    string Title,
    string Description,
    string ImageSrc,
    string ImageAlt);
