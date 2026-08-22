using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Duplicates;

/// <summary>One asset's membership of a duplicate set.</summary>
public sealed class DuplicateMember
{
    public int Id { get; set; }

    public int DuplicateSetId { get; set; }

    public DuplicateSet? DuplicateSet { get; set; }

    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    public DuplicateRole Role { get; set; }

    /// <summary>
    /// Perceptual distance from the keeper, for near matches. Zero for exact
    /// matches and for the keeper itself.
    /// </summary>
    public int Distance { get; set; }
}
