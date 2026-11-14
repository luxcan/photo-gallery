namespace PhotoGallery.Domain.Duplicates;

/// <summary>A group of assets found to be the same photo or video.</summary>
public sealed class DuplicateSet
{
    public int Id { get; set; }

    public DuplicateKind Kind { get; set; }

    public DateTime DetectedUtc { get; set; }

    /// <summary>True once you have acted on this set, so it stops being offered.</summary>
    public bool IsResolved { get; set; }

    public List<DuplicateMember> Members { get; } = [];

    /// <summary>Bytes freed if every redundant member were removed.</summary>
    public long RedundantBytes =>
        Members.Where(m => m.Role == DuplicateRole.Redundant)
               .Sum(m => m.Asset?.Length ?? 0L);

    /// <summary>
    /// Marks the suggested copy as the keeper and the rest redundant, or every
    /// copy undecided when nothing is suggested.
    /// </summary>
    /// <param name="keeperAssetId">
    /// The copy <see cref="KeeperPolicy"/> suggests, or null when nothing
    /// separates the copies - which is always the answer for identical files.
    /// </param>
    /// <remarks>
    /// Undecided rather than all redundant when there is no keeper, because
    /// redundant is what setting aside acts on: a set nobody chose a copy in
    /// must never be one where every copy is offered up.
    /// </remarks>
    /// <exception cref="ArgumentException">The keeper is not one of the members.</exception>
    public void Suggest(int? keeperAssetId)
    {
        if (keeperAssetId is int keeper && Members.All(member => member.AssetId != keeper))
        {
            throw new ArgumentException(
                $"Asset {keeper} is not a member of this set.", nameof(keeperAssetId));
        }

        foreach (DuplicateMember member in Members)
        {
            member.Role = keeperAssetId is null
                ? DuplicateRole.Undecided
                : member.AssetId == keeperAssetId
                    ? DuplicateRole.Keeper
                    : DuplicateRole.Redundant;
        }
    }
}
