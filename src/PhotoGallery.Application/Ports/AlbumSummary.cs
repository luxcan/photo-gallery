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
/// <para>What "recently used" means for a list of albums to file into.
/// <c>StartUtc</c> cannot answer that: for a proposal it is the span of the
/// photographs, so a 2019 holiday added to last night sinks to the bottom of
/// the picker.</para>
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
    DateTime? LastAddedUtc = null);
