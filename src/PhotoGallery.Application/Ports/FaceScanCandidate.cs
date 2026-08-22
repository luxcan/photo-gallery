namespace PhotoGallery.Application.Ports;

/// <summary>
/// A photo the face pass may need to read, and the rendition it would read.
/// </summary>
/// <param name="DetectedUtc">
/// When faces were last found in it, or null if never. Carried so the pass can
/// compare that against the preview actually on disk: a rendition can be
/// rewritten under the same name - it is named after the original's content, and
/// the original has not changed - and faces recorded against the image before it
/// then describe something that is no longer there.
/// </param>
public sealed record FaceScanCandidate(
    int AssetId, string ThumbnailName, DateTime? DetectedUtc = null);
