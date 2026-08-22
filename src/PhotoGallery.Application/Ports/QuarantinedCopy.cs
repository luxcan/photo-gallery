namespace PhotoGallery.Application.Ports;

/// <summary>A copy that has been set aside, and everything needed to put it back.</summary>
/// <remarks>
/// The original location is not stored anywhere separately: it is the photo
/// source's root joined to the relative path, which is the same pair the
/// quarantine folder is laid out by. That is what makes restoring mechanical
/// rather than a lookup that could go stale.
/// </remarks>
public sealed record QuarantinedCopy(
    int AssetId,
    int PhotoSourceId,
    string RelativePath,
    string OriginalFullPath,
    long Length,
    DateTime QuarantinedUtc);
