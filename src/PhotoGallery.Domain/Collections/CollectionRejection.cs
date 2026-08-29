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

    /// <summary>
    /// The machine that refused it, or empty for this library's own answer.
    /// </summary>
    /// <remarks>
    /// Empty rather than this machine's own id, so that nothing local has to
    /// remember to stamp it and a row written before this column existed is
    /// correctly attributed. It is resolved on the way out, where the machine
    /// publishing knows who it is.
    ///
    /// <para>It has to survive, because a machine publishes everything it holds
    /// rather than only what it refused itself. An answer that lost its author
    /// passing through would be republished as the forwarder's own and would
    /// start settling ties it has no business settling - and two machines that
    /// answered in the same second would then disagree about which of them won,
    /// depending on who had passed the answer on. Three laptops would never
    /// converge.</para>
    /// </remarks>
    public Guid RejectedBy { get; set; }
}
