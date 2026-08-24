namespace PhotoGallery.Application.Ports;

/// <summary>A photo the thumbnail pass may need to do, with the path to read.</summary>
/// <remarks>
/// <see cref="ThumbnailName"/> is what the row claims, which is not the same as
/// what exists: a working folder can be copied, cleaned or synced without its
/// index, leaving names pointing at files that are gone. Only the pass can tell
/// the difference, because only it can see the disk.
/// </remarks>
/// <param name="Rotation">
/// A turn the user asked for, which the pass reapplies. Renditions are derived
/// and get rebuilt whenever the file changes or the cache is cleared, so without
/// carrying it here a photograph straightened once would come back upside down.
/// </param>
public readonly record struct PendingThumbnail(
    int AssetId, string FullPath, string? ThumbnailName, int Rotation);
