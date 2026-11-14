namespace PhotoGallery.Domain.Duplicates;

/// <summary>The ladder's answer for one set of copies.</summary>
/// <param name="KeeperId">The copy suggested, or null when nothing separates them.</param>
/// <param name="DecidedBy">The rung that left one copy standing, or null with no keeper.</param>
/// <param name="Copies">One verdict per copy, in the order the copies were given.</param>
public sealed record KeeperChoice(
    int? KeeperId,
    KeeperCriterion? DecidedBy,
    IReadOnlyList<CopyVerdict> Copies)
{
    /// <summary>The verdict for one copy.</summary>
    /// <exception cref="KeyNotFoundException">The copy was not in the set.</exception>
    public CopyVerdict For(int assetId) =>
        Copies.FirstOrDefault(copy => copy.AssetId == assetId)
        ?? throw new KeyNotFoundException($"Asset {assetId} was not one of the copies weighed.");
}
