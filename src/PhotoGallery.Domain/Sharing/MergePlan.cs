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
    IReadOnlyList<AlbumMove> Moves,
    IReadOnlyList<AlbumRejection> Rejections,
    IReadOnlyList<SharedEra> Eras,
    HeldAnswers Held,
    IReadOnlyList<PersonJoin> Joins,
    IReadOnlyList<RefusedSet> Refused)
{
    public static MergePlan Nothing { get; } =
        new([], [], [], [], [], [], [], [], [], [], HeldAnswers.None, [], []);

    /// <summary>Whether this merge would change anything at all.</summary>
    /// <remarks>
    /// Joins and refusals are deliberately not counted. A join is an offer the
    /// user has not accepted and a refusal is something that did not happen, so a
    /// merge reporting either of them and nothing else has still changed nothing.
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
        && Held.Count == 0;
}
