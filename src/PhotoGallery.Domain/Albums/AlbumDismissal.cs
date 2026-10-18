namespace PhotoGallery.Domain.Albums;

/// <summary>
/// The last time somebody dismissed the suggestion for a run of days.
/// </summary>
/// <remarks>
/// Dismissing a suggestion here was only ever its refusals, one per photograph,
/// and the row going - so nothing said, about the album as a whole, that it had
/// been refused or when. That was enough while an answer stayed on the laptop
/// it was given on. It is not enough once keeping travels: a keep on one laptop
/// and a dismissal on another are two answers about the same days, the later
/// one has to stand, and a dismissal with no moment cannot be later than
/// anything.
///
/// <para>Its own table rather than a tombstone on the album, deliberately. A
/// tombstone that keeps its run of days means "never suggest these days again",
/// which is what deleting a kept album says - and the rebuild reads it that
/// way. Dismissing a suggestion has never meant that: what is left of those
/// days can still be offered afresh. Kept apart, the two answers cannot be
/// mistaken for each other, and this one never stands in the way of the unique
/// key on a suggestion's run of days.</para>
///
/// <para>One row per run of days, holding the latest dismissal anybody in the
/// house has made of it. It is never deleted, even once a later keep has beaten
/// it: it is still an answer, and the next laptop to hear of that keep has to
/// be able to weigh the two.</para>
/// </remarks>
public sealed class AlbumDismissal
{
    /// <summary>The run of days the dismissed suggestion covered.</summary>
    public required string ProposalKey { get; set; }

    /// <summary>When it was dismissed.</summary>
    public DateTime DismissedUtc { get; set; }

    /// <summary>
    /// The machine that dismissed it, or empty for this library's own answer.
    /// </summary>
    /// <remarks>
    /// Empty rather than this machine's own id, for the reason a refusal's is:
    /// nothing local has to remember to stamp it, and it is resolved on the way
    /// out, where the machine publishing knows who it is.
    /// </remarks>
    public Guid DismissedBy { get; set; }
}
