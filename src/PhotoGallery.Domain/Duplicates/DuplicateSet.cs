using PhotoGallery.Domain.Assets;

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
    /// Applies <see cref="KeeperPolicy"/> across the current members, marking
    /// exactly one keeper and the rest redundant.
    /// </summary>
    public void AssignRoles()
    {
        List<Asset> assets = Members
            .Select(m => m.Asset ?? throw new InvalidOperationException(
                $"Member {m.Id} has no asset loaded; roles cannot be assigned."))
            .ToList();

        Asset keeper = KeeperPolicy.ChooseKeeper(assets);
        foreach (DuplicateMember member in Members)
        {
            member.Role = ReferenceEquals(member.Asset, keeper)
                ? DuplicateRole.Keeper
                : DuplicateRole.Redundant;
        }
    }
}
