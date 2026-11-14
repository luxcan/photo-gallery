namespace PhotoGallery.Measure;

/// <summary>One copy in one near-duplicate set, as the database describes it.</summary>
internal sealed record NearCopy(
    int SetId,
    bool SetResolved,
    int AssetId,
    string Path,
    int? Width,
    int? Height,
    long Bytes,
    bool HasTakenUtc,
    bool HasGps,
    string Role,
    bool Quarantined,
    string? ThumbnailName);
