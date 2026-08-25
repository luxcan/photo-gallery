namespace PhotoGallery.Domain.Collections;

/// <summary>
/// One photograph as the clusterer needs it: when it was taken, and where if
/// that is known.
/// </summary>
/// <remarks>
/// A struct with four fields rather than the asset row, so grouping 9,544
/// photographs costs one array rather than materialising every column the
/// index holds.
/// </remarks>
public readonly record struct DatedPhoto(
    int AssetId, DateTime TakenUtc, double? Latitude, double? Longitude)
{
    /// <summary>Whether this one can answer where it was.</summary>
    public bool HasPlace => Latitude is not null && Longitude is not null;
}
