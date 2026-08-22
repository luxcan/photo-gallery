namespace PhotoGallery.Domain.Library;

/// <summary>
/// One place photos come from: a folder on this PC, an external drive, or a
/// network share. A library can aggregate any number of them.
/// </summary>
/// <remarks>
/// Sources are only ever read. Scanning walks each source in turn, and every
/// asset remembers which source it came from, so a source can be detached
/// without disturbing the others.
/// </remarks>
public sealed class PhotoSource
{
    public int Id { get; set; }

    /// <summary>Root of the source, e.g. <c>D:\Camera Dumps</c> or a UNC path.</summary>
    public required string Path { get; set; }

    public DateTime AddedUtc { get; set; }

    public DateTime? LastScanUtc { get; set; }
}
