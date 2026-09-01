using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Application.Ports;

/// <summary>One album as a list reads it: what it is called, and how big.</summary>
/// <param name="CoverThumbnailName">
/// The rendition to draw for it, or null when its cover has gone. An album
/// with no picture is not shown - the same rule a duplicate set follows when its
/// keeper disappears.
/// </param>
public sealed record AlbumSummary(
    int Id,
    string Name,
    DateTime StartUtc,
    DateTime EndUtc,
    AlbumKind Kind,
    AlbumOrigin Origin,
    int PhotoCount,
    string? CoverThumbnailName);
