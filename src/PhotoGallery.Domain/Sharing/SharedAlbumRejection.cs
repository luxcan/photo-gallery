namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Somebody saying a photograph does not belong in an album covering these days.
/// </summary>
/// <remarks>
/// Keyed on the run of days rather than on the album row, for the reason the
/// local table is: a proposed row is derived, and the rebuild that renumbers it
/// would otherwise forget every dismissal.
///
/// <para>Rejections only ever accumulate, so two machines never disagree about
/// one - the merge is a union, not a contest.</para>
/// </remarks>
public sealed record SharedAlbumRejection(
    AssetKey Photo,
    string ProposalKey,
    DateTime RejectedUtc,
    Guid DecidedBy);
