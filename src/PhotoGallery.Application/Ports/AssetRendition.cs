namespace PhotoGallery.Application.Ports;

/// <summary>
/// An indexed file and the cached copy it claims - all that detaching needs to
/// know: which files to delete, and which row to remove once they have gone.
/// </summary>
public readonly record struct AssetRendition(int AssetId, string? ThumbnailName);
