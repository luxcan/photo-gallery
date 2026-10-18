namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Somebody dismissing the suggestion for a run of days, as one machine tells
/// another about it.
/// </summary>
/// <remarks>
/// About the album as a whole, where a <see cref="SharedAlbumRejection"/> is
/// about one photograph in it. Both travel: the refusals keep the photographs
/// out of those days wherever they are suggested, and this is what a keep made
/// on another laptop is weighed against.
///
/// <para>It competes only with keeps. Against a suggestion nobody kept it does
/// nothing the refusals were not already doing, and a deletion of a kept
/// suggestion beats it as it beats every keep.</para>
/// </remarks>
/// <param name="ProposalKey">The run of days the dismissed suggestion covered.</param>
/// <param name="DismissedUtc">When it was dismissed.</param>
/// <param name="DecidedBy">The machine that dismissed it.</param>
public sealed record SharedAlbumDismissal(
    string ProposalKey,
    DateTime DismissedUtc,
    Guid DecidedBy);
