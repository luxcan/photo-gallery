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
/// <param name="Cover">
/// The photograph somebody chose for it, or null where nobody has and the app is
/// still working one out.
/// </param>
/// <param name="CoverChosenUtc">
/// When they chose it. Null loses to any date, the same way a name nobody typed
/// does.
/// </param>
/// <remarks>
/// <strong>Only a chosen cover travels.</strong> The one the app works out is a
/// guess, and the other machine makes its own from its own faces - so sending it
/// would be one library's guess beating another library's equally good guess,
/// and then beating it again on every merge afterwards. It is the rule
/// <see cref="DecisionSet.WithoutProposals"/> follows for a name, arriving at the
/// same place from the other end.
/// </remarks>
public sealed record SharedAlbum(
    Guid PublicId,
    string Name,
    AlbumOrigin Origin,
    string? ProposalKey,
    DateTime? NamedUtc,
    DateTime? DeletedUtc,
    Guid? Shelf = null,
    DateTime? ShelvedUtc = null,
    AssetKey? Cover = null,
    DateTime? CoverChosenUtc = null);
