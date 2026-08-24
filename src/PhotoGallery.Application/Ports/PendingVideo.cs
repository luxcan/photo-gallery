namespace PhotoGallery.Application.Ports;

/// <summary>
/// A video the keyframe pass may need to read, and what its frames would be
/// named.
/// </summary>
/// <remarks>
/// Carries the three facts a scan already knows - path, size and modified time -
/// because the names of this video's frames are worked out from them, and the
/// pass decides what is outstanding by asking the disk whether those files are
/// there. The row's own claim is not enough: a working folder can be copied or
/// cleaned without its index.
/// </remarks>
public readonly record struct PendingVideo(
    int AssetId, string FullPath, string RelativePath, long Length, DateTime ModifiedUtc);
