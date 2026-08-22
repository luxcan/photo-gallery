using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Application.Ports;

/// <summary>What a thumbnail pass learned about one asset.</summary>
/// <remarks>
/// Everything here comes from the single decode of the original that the pass
/// performs. Nothing on it justifies reading the file a second time.
/// </remarks>
/// <param name="Latitude">
/// Where the photograph was taken, or null where the file said nothing. Written
/// on every prepare because it is re-derived from the file each time and cannot
/// disagree with it - unlike the place those coordinates resolve to, which this
/// deliberately does not carry.
/// </param>
public readonly record struct ThumbnailUpdate(
    int AssetId,
    string ThumbnailName,
    int Width,
    int Height,
    PerceptualHash? PerceptualHash,
    DateTime? TakenUtc,
    string? ContentHash,
    double? Latitude = null,
    double? Longitude = null);
