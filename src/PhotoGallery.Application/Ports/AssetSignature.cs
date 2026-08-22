namespace PhotoGallery.Application.Ports;

/// <summary>
/// The minimum needed to decide whether an indexed file still matches what is
/// on disk: its identity, its size, and its two dates.
/// </summary>
/// <param name="ThumbnailName">
/// The rendition this row currently claims, so a scan that finds the file
/// changed can delete what the old bytes produced. Renditions are named after
/// the picture's content, so a changed file is written under a new name and the
/// previous pair would otherwise stay on disk with nothing referring to it.
/// </param>
/// <param name="IsQuarantined">
/// True when this copy has been set aside as redundant. Its file is genuinely
/// not where the row says, so a scan must not conclude it has been deleted and
/// take the row away - that would destroy the only thing that knows how to put
/// it back.
/// </param>
public readonly record struct AssetSignature(
    int AssetId,
    long Length,
    DateTime ModifiedUtc,
    DateTime CreatedUtc,
    string? ThumbnailName,
    bool IsQuarantined = false)
{
    /// <summary>
    /// True when the file on disk looks untouched, so the scan can skip it
    /// without opening it. Timestamps are compared to the second because some
    /// file systems and network shares round them.
    /// </summary>
    /// <remarks>
    /// Creation time counts as part of the identity, so a file that has been
    /// replaced in place is noticed even if the copy preserved its size and its
    /// modified date.
    ///
    /// <para>The cost of including it: creation time is rewritten by copying,
    /// restoring from backup, or a sync that does not preserve timestamps. When
    /// that happens the file reads as changed although its bytes did not move,
    /// and its thumbnail, capture date and hashes are discarded and rebuilt -
    /// which for this library means reading 24.8 GB again. Measured evidence
    /// that it does happen: 3,000 photos here carry just 13 distinct creation
    /// days, one per bulk copy. Drop the third comparison to undo this.</para>
    /// </remarks>
    public bool Matches(long length, DateTime modifiedUtc, DateTime createdUtc) =>
        Length == length
        && Math.Abs((ModifiedUtc - modifiedUtc).TotalSeconds) < 2
        && (!KnowsCreatedDate || Math.Abs((CreatedUtc - createdUtc).TotalSeconds) < 2);

    /// <summary>
    /// Whether this row was indexed after the creation date began being recorded.
    /// </summary>
    /// <remarks>
    /// Rows that predate it hold the sentinel, and an unknown date is not a
    /// difference. Treating it as one would have declared all 16,225 files
    /// changed on the first scan after the upgrade, throwing away every
    /// thumbnail, capture date and hash the app had spent two hours building.
    /// </remarks>
    private bool KnowsCreatedDate => CreatedUtc != default;
}
