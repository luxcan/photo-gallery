namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Everything one merge would change, worked out without changing any of it.
/// </summary>
/// <remarks>
/// <strong>Only what differs.</strong> A merge that would leave the library
/// exactly as it is produces an empty plan, which is how "merging twice changes
/// nothing the second time" is a property you can look at rather than a claim
/// somebody has to trust. It is also what lets the receiving machine report what
/// changed by count and by kind: a merge that says nothing is a merge nobody can
/// trust or undo.
///
/// <para>Nothing here removes a photograph from a library, whatever the other
/// machine did with its own files. There is deliberately no field for it.</para>
/// </remarks>
/// <param name="Withdrawn">
/// Names this library had confirmed that no longer stand, because somebody has
/// since said that face is a different person or nobody at all. A face is one
/// person, so leaving them would make it two.
/// </param>
public sealed record MergePlan(
    IReadOnlyList<SharedPerson> People,
    IReadOnlyList<FaceAnswer> Answers,
    IReadOnlyList<FaceAnswer> Withdrawn,
    IReadOnlyList<StrangerFace> Strangers,
    IReadOnlyList<FaceKey> Recognised,
    IReadOnlyList<PhotoTurn> Turns,
    IReadOnlyList<SharedAlbum> Albums,
    IReadOnlyList<SharedAlbumMove> Moves,
    IReadOnlyList<SharedAlbumRejection> Rejections,
    IReadOnlyList<SharedEra> Eras,
    IReadOnlyList<SharedCollection> Collections,
    HeldAnswers Held,
    IReadOnlyList<PersonJoin> Joins,
    IReadOnlyList<RefusedSet> Refused,
    IReadOnlyList<SourceLink> Links,
    IReadOnlyDictionary<Guid, Guid> Renames,
    IReadOnlyList<PairingProposal> Pairings)
{
    public static MergePlan Nothing { get; } =
        new([], [], [], [], [], [], [], [], [], [], [], HeldAnswers.None, [], [],
            [], new Dictionary<Guid, Guid>(), []);

    /// <summary>
    /// Waiting answers a sweep is done with although nothing is written for them.
    /// </summary>
    /// <remarks>
    /// Only <see cref="DecisionMerge.Rejoin"/> fills this. Three kinds of answer
    /// end here: one this library already holds, one a later answer has since
    /// beaten, and one about an album this library has thrown away. In each the
    /// answer that stands is already in this library and nothing about it can
    /// change - so the held row can go - but nothing was applied, and a count
    /// that said otherwise would make "applied" mean "no longer waiting".
    /// </remarks>
    public HeldAnswers Settled { get; init; } = HeldAnswers.None;

    /// <summary>
    /// Waiting moves whose photograph is here, into an album this library has no
    /// row it would put the photograph in.
    /// </summary>
    /// <remarks>
    /// Kept rather than settled, because nothing here has answered them. The
    /// album is one this library has never held under that name, or a suggestion
    /// nobody here has kept - and the held row may be the only copy of the
    /// answer, so letting it go would be losing it. It comes good when this
    /// library comes to hold the same answer some other way, which is the next
    /// sync placing the photograph from its author's own set.
    /// </remarks>
    public IReadOnlyList<SharedAlbumMembership> Unplaced { get; init; } = [];

    /// <summary>Whether this merge would change anything at all.</summary>
    /// <remarks>
    /// Joins and refusals are deliberately not counted. A join is an offer the
    /// user has not accepted and a refusal is something that did not happen, so a
    /// merge reporting either of them and nothing else has still changed nothing.
    /// Answers a sweep settled or could not place are not counted either, for the
    /// same reason: neither writes anything.
    /// </remarks>
    public bool ChangesNothing =>
        People.Count == 0
        && Answers.Count == 0
        && Withdrawn.Count == 0
        && Strangers.Count == 0
        && Recognised.Count == 0
        && Turns.Count == 0
        && Albums.Count == 0
        && Moves.Count == 0
        && Rejections.Count == 0
        && Eras.Count == 0
        && Collections.Count == 0
        && Held.Count == 0

        // A rename is a change, and a big one: every key in this library's own
        // answers means a different photograph afterwards. Links on their own
        // are not - a link this library has no source in is a fact about two
        // other machines, worth keeping and passing on and nothing more.
        && Renames.Count == 0;
}
