namespace PhotoGallery.Domain.Sharing;

/// <summary>One photograph's place in one album.</summary>
/// <remarks>
/// A photograph belongs to at most one album, which the schema enforces, so two
/// machines putting one picture in two albums is a disagreement rather than two
/// facts. The later <see cref="AddedUtc"/> wins and the app says which album it
/// left, because a photograph quietly moving is the sort of thing somebody
/// notices weeks later and cannot explain.
/// </remarks>
public sealed record AlbumMembership(
    AssetKey Photo,
    Guid Album,
    DateTime AddedUtc,
    Guid DecidedBy);
