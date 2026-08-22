namespace PhotoGallery.Application.Ports;

/// <summary>
/// Where redundant copies go instead of being deleted.
/// </summary>
/// <remarks>
/// The layout mirrors the library: <c>quarantine\&lt;source id&gt;\&lt;relative
/// path&gt;</c>. That is not decoration - it means putting a file back needs no
/// record of where it came from beyond what the row already says, so restoring
/// cannot be defeated by a lost or stale manifest.
/// </remarks>
public interface IQuarantineStore
{
    /// <summary>Where a given copy would sit once set aside.</summary>
    string PathFor(int photoSourceId, string relativePath);

    /// <summary>
    /// Moves a file out of the library, or reports why it could not.
    /// </summary>
    /// <remarks>
    /// Copy first, verify, and only then delete the original. A move that fails
    /// halfway across a network share must leave the library intact - this
    /// feature's whole promise is that nothing is lost.
    /// </remarks>
    /// <param name="contentHash">
    /// The digest the copy must have, where one is known. Length alone cannot
    /// tell an intact copy from one the network corrupted without truncating,
    /// and the original is deleted on the strength of this answer - so a file
    /// that arrives wrong should be refused rather than swapped for the good one.
    /// Every file a duplicate pass sets aside has a hash already: it is what
    /// proved the file a duplicate.
    /// </param>
    Task<bool> PutAsync(
        string originalFullPath,
        int photoSourceId,
        string relativePath,
        string? contentHash = null,
        CancellationToken cancellationToken = default);

    /// <summary>Puts a file back where it came from.</summary>
    /// <returns>
    /// True when the file is at its original location afterwards, including when
    /// it was already there.
    /// </returns>
    Task<bool> TakeBackAsync(
        string originalFullPath,
        int photoSourceId,
        string relativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a human-readable record of what was set aside and where it came
    /// from.
    /// </summary>
    /// <remarks>
    /// Not what restore reads - that is derived from the layout. This is for the
    /// person who opens the quarantine folder in a year's time and wants to know
    /// what they are looking at.
    /// </remarks>
    Task WriteManifestAsync(
        IReadOnlyList<QuarantinedCopy> copies, CancellationToken cancellationToken = default);
}
