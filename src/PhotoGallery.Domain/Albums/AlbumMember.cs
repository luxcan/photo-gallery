using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Albums;

/// <summary>One photograph's place in one album.</summary>
/// <remarks>
/// <see cref="AssetId"/> is the whole primary key, and that single fact is the
/// rule that a photograph belongs to at most one album. It is enforced
/// here rather than in a handler because a handler can forget: three paths
/// write memberships - the rebuild, making an album, and adding to one -
/// and the database holds whichever of them is wrong.
///
/// <para>Deliberately not the shape <c>DuplicateMember</c> uses, which carries
/// its own id and a composite index. That exists because one photograph can
/// legitimately belong to several duplicate sets. This one cannot.</para>
/// </remarks>
public sealed class AlbumMember
{
    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    public int AlbumId { get; set; }

    public Album? Album { get; set; }

    /// <summary>When it joined, so a rebuild can leave alone what a user added.</summary>
    public DateTime AddedUtc { get; set; }

    /// <summary>
    /// The machine that put it here, or empty for this library's own answer.
    /// </summary>
    /// <remarks>
    /// Empty rather than this machine's own id, so that nothing local has to
    /// remember to stamp it and a row written before this column existed is
    /// correctly attributed. It is resolved on the way out, where the machine
    /// publishing knows who it is.
    ///
    /// <para>It has to survive, because a machine publishes everything it holds
    /// rather than only what it put it hereself. An answer that lost its author
    /// passing through would be republished as the forwarder's own and would
    /// start settling ties it has no business settling - and two machines that
    /// answered in the same second would then disagree about which of them won,
    /// depending on who had passed the answer on. Three laptops would never
    /// converge.</para>
    /// </remarks>
    public Guid AddedBy { get; set; }
}
