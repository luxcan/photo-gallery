using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Collections;

/// <summary>One photograph's place in one collection.</summary>
/// <remarks>
/// <see cref="AssetId"/> is the whole primary key, and that single fact is the
/// rule that a photograph belongs to at most one collection. It is enforced
/// here rather than in a handler because a handler can forget: three paths
/// write memberships - the rebuild, making a collection, and adding to one -
/// and the database holds whichever of them is wrong.
///
/// <para>Deliberately not the shape <c>DuplicateMember</c> uses, which carries
/// its own id and a composite index. That exists because one photograph can
/// legitimately belong to several duplicate sets. This one cannot.</para>
/// </remarks>
public sealed class CollectionMember
{
    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    public int CollectionId { get; set; }

    public Collection? Collection { get; set; }

    /// <summary>When it joined, so a rebuild can leave alone what a user added.</summary>
    public DateTime AddedUtc { get; set; }
}
