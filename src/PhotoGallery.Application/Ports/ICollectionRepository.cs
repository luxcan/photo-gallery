namespace PhotoGallery.Application.Ports;

/// <summary>Reads and writes the shelves of albums, and what is on them.</summary>
/// <remarks>
/// Deliberately small. A collection has no rule, no members of its own and no
/// pass that writes it, so there is nothing here but making one, naming it,
/// saying which albums are on it and taking it away.
/// </remarks>
public interface ICollectionRepository
{
    /// <summary>Every collection, in the order the band shows them.</summary>
    /// <remarks>
    /// By name, because a theme has no place on a calendar and somebody
    /// scanning the band is looking for a word.
    /// </remarks>
    Task<IReadOnlyList<CollectionSummary>> GetAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Makes one, and returns its id.</summary>
    Task<int> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Renames one, and records that the name was typed again.</summary>
    Task RenameAsync(int collectionId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one, leaving every album that was on it on no shelf at all.
    /// </summary>
    /// <remarks>
    /// A tombstone rather than a delete, so a merge from a machine that still
    /// holds it cannot put it back. Nothing on the shelf is destroyed - the
    /// same rule removing an album follows for its photographs.
    /// </remarks>
    Task DeleteAsync(int collectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Says which albums are on a collection, and takes off any that are not
    /// named.
    /// </summary>
    /// <remarks>
    /// The whole shelf in one call rather than an add and a remove, because the
    /// screen asks the question that way: a tick list is the shelf, and pressing
    /// Save once is what a person did. An album named here leaves whatever other
    /// shelf it was on, since an album is on at most one.
    ///
    /// <para>A suggestion named here is kept on the way in. Putting a proposal
    /// on a shelf is deciding it is worth keeping, and asking somebody to keep
    /// it first and then come back and find it is the procedure this screen
    /// exists to avoid. The result says how many, because it is a change to
    /// their library and not a detail.</para>
    /// </remarks>
    Task<CollectionFillResult> SetAlbumsAsync(
        int collectionId,
        IReadOnlyList<int> albumIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts one album on one collection, or on none, and answers with the name
    /// of the collection it came off.
    /// </summary>
    /// <remarks>
    /// The other direction of <see cref="SetAlbumsAsync"/>, for the album's own
    /// panel: there the question is "which shelf is this album on", and naming
    /// the whole shelf to answer it would take every other album off.
    ///
    /// <para>Null for <paramref name="collectionId"/> takes it off whatever it
    /// was on. The name that comes back is what the screen says about a move
    /// nobody asked for - an album is on one collection, so choosing this one is
    /// leaving that one, and the same rule the photographs follow is said out
    /// loud rather than enforced in silence.</para>
    /// </remarks>
    Task<string?> SetAlbumCollectionAsync(
        int albumId,
        int? collectionId,
        CancellationToken cancellationToken = default);
}
