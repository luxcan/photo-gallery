using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Application.Ports;

/// <summary>One album as a list reads it: what it is called, and how big.</summary>
/// <param name="CoverThumbnailName">
/// The rendition to draw for it, or null when its cover has gone. An album
/// with no picture is not shown - the same rule a duplicate set follows when its
/// keeper disappears.
/// </param>
/// <param name="CollectionId">
/// The shelf this album is on, or null while it is on none. Carried on the
/// summary rather than asked for separately because the wall needs it every
/// time it is drawn: the top level shows the albums on no shelf, and an open
/// collection shows its own.
/// </param>
/// <param name="LastAddedUtc">
/// When a photograph was last put in it, or null while it holds none.
/// </param>
/// <param name="NamedUtc">
/// When the user made this album or last renamed it, or null while the name is
/// still the app's own.
/// </param>
public sealed record AlbumSummary(
    int Id,
    string Name,
    DateTime StartUtc,
    DateTime EndUtc,
    AlbumKind Kind,
    AlbumOrigin Origin,
    int PhotoCount,
    string? CoverThumbnailName,
    int? CollectionId = null,
    DateTime? LastAddedUtc = null,
    DateTime? NamedUtc = null)
{
    /// <summary>
    /// When this album was last used: made, renamed, or filed into, whichever
    /// happened last.
    /// </summary>
    /// <remarks>
    /// What "recent" means for a list of albums to file into, and it takes both
    /// halves. Filing alone leaves an album just created sitting at the bottom
    /// with nothing in it - which is the one somebody is most likely to have
    /// made in order to file into it. Creation alone forgets that a holiday from
    /// 2019 added to last night is the one in hand.
    ///
    /// <para><c>StartUtc</c> can answer neither: for a proposal it is the span
    /// of the photographs in it, so the dates it carries are the camera's rather
    /// than the user's.</para>
    /// </remarks>
    public DateTime? LastUsedUtc => (LastAddedUtc, NamedUtc) switch
    {
        (null, DateTime named) => named,
        (DateTime added, null) => added,
        (DateTime added, DateTime named) => added > named ? added : named,
        _ => null,
    };
}
