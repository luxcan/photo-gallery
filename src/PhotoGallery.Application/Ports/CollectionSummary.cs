using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Application.Ports;

/// <summary>One collection as a list reads it: what it is called, and how big.</summary>
/// <param name="CoverThumbnailName">
/// The rendition to draw for it, or null when its cover has gone. A collection
/// with no picture is not shown - the same rule a duplicate set follows when its
/// keeper disappears.
/// </param>
public sealed record CollectionSummary(
    int Id,
    string Name,
    DateTime StartUtc,
    DateTime EndUtc,
    CollectionKind Kind,
    CollectionOrigin Origin,
    int PhotoCount,
    string? CoverThumbnailName);
