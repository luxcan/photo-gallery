namespace PhotoGallery.Domain.Sharing;

/// <summary>A photograph changing which album it is in.</summary>
/// <remarks>
/// Carries where it came from as well as where it is going, because a
/// photograph quietly moving is the sort of thing somebody notices weeks later
/// and cannot explain. The merge is allowed to settle it; it is not allowed to
/// settle it silently.
/// </remarks>
/// <param name="From">The album it leaves, or null when it was in none.</param>
/// <param name="DecidedBy">
/// The machine that put it there, kept rather than restamped as this one. A
/// machine publishes everything it holds, so an answer that lost its author on
/// the way through would be republished as this library's own and would start
/// winning ties it has no business winning.
/// </param>
public sealed record SharedAlbumMove(
    AssetKey Photo,
    Guid? From,
    Guid To,
    DateTime AddedUtc,
    Guid DecidedBy);
