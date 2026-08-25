using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Collections;

/// <summary>
/// A photograph the user has said does not belong in a collection covering
/// these days.
/// </summary>
/// <remarks>
/// Keyed on the span rather than on the collection row, so it survives the
/// rebuild that replaces that row - see <see cref="ProposalKey"/>. Rejecting a
/// photograph from one span never affects another: the same photograph can
/// still be proposed for a different occasion, which is what "that photo, that
/// album" means.
///
/// <para>A date rather than a flag, following the way an ignored face is
/// recorded, so a sweep of rejections can be shown and undone rather than being
/// a silent no.</para>
/// </remarks>
public sealed class CollectionRejection
{
    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    public required string ProposalKey { get; set; }

    public DateTime RejectedUtc { get; set; }
}
