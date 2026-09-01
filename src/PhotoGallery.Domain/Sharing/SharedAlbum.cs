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
public sealed record SharedAlbum(
    Guid PublicId,
    string Name,
    AlbumOrigin Origin,
    string? ProposalKey,
    DateTime? NamedUtc,
    DateTime? DeletedUtc);
