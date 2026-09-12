using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Domain.Sharing;

/// <summary>An album, as one machine tells another about it.</summary>
/// <param name="ProposalKey">
/// The run of days a proposal was built from, or null for an album somebody
/// made. A renamed proposal travels on this rather than on its identity: the row
/// is rebuilt and renumbered, and the days are what survive that.
/// </param>
/// <param name="NamedUtc">
/// When somebody typed the name, or null while it is still the app's own. Null
/// loses to any date.
/// </param>
/// <param name="Shelf">
/// The collection this album sits on, by that collection's public identity, or
/// null while it sits on none.
/// </param>
/// <param name="ShelvedUtc">
/// When somebody last shelved or unshelved it, or null while nobody ever has.
/// Settled on its own rather than riding on <paramref name="NamedUtc"/>:
/// renaming an album and moving it are two decisions made at two moments, and
/// one date for both hands every argument about shelves to whoever typed a name
/// last.
/// </param>
public sealed record SharedAlbum(
    Guid PublicId,
    string Name,
    AlbumOrigin Origin,
    string? ProposalKey,
    DateTime? NamedUtc,
    DateTime? DeletedUtc,
    Guid? Shelf = null,
    DateTime? ShelvedUtc = null);
