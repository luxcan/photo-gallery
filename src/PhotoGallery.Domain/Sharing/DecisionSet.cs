using PhotoGallery.Domain.People;

namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Everything one machine has been told or has decided, written whole.
/// </summary>
/// <remarks>
/// <strong>State, not a log.</strong> Each machine publishes its entire decision
/// set - 469 KB on this library - and every merge is a full reconciliation.
/// There are no watermarks to drift, no journal to compact and no history to
/// propagate, which is why merging twice changes nothing and a merge stopped
/// halfway simply finishes next time.
///
/// <para><strong>Everything it holds, not only what it decided itself.</strong>
/// Over a shared folder that changes nothing, because everybody reads
/// everybody's file. Over a direct connection it is the difference between
/// working and not: if Ana's laptop only ever pairs with Dad's, it receives
/// Mum's answers solely because Dad's published set carries them. Forwarding
/// what you were told is what makes three machines converge with no machinery
/// for it, and it is safe only because every answer keeps when it was decided
/// and by whom rather than who handed it over.</para>
///
/// <para>Nothing here describes how anybody looks at their pictures. The theme,
/// the cell size, the sort order and the nav state have no field, which is the
/// cheapest possible guarantee that they never travel.</para>
/// </remarks>
/// <param name="Sources">
/// The shared ids this machine holds sources for. Derivable from the keys, and
/// carried anyway: a machine with answers about nothing in common would
/// otherwise be indistinguishable from one with no answers, and the two need
/// different things said about them.
/// </param>
public sealed record DecisionSet(
    MachineIdentity Machine,
    DateTime WrittenUtc,
    IReadOnlyList<Guid> Sources,
    IReadOnlyList<SharedPerson> People,
    IReadOnlyList<FaceAnswer> Answers,
    IReadOnlyList<StrangerFace> Strangers,
    IReadOnlyList<PhotoTurn> Turns,
    IReadOnlyList<SharedAlbum> Albums,
    IReadOnlyList<AlbumMembership> Memberships,
    IReadOnlyList<AlbumRejection> Rejections,
    IReadOnlyList<SharedEra> Eras)
{
    /// <summary>An empty set from a machine, for a library that has decided nothing.</summary>
    public static DecisionSet Empty(MachineIdentity machine, DateTime writtenUtc) =>
        new(machine, writtenUtc, [], [], [], [], [], [], [], [], []);

    /// <summary>
    /// The same set with the app's own guesses taken out, which is what is
    /// published.
    /// </summary>
    /// <remarks>
    /// 1,359 of this library's 10,733 assignments are proposals. The other
    /// machine will make its own, and better ones, from the confirmations it has
    /// just received - so sending a guess as though it were an answer buys
    /// nothing and is how one wrong proposal becomes permanent across a whole
    /// family.
    ///
    /// <para>They are still needed locally, which is why this is a step rather
    /// than a rule of the type: a confirmation arriving from another machine has
    /// to be able to beat a proposal sitting here, and it can only do that if the
    /// merge can see it.</para>
    /// </remarks>
    public DecisionSet WithoutProposals() => this with
    {
        Answers = [.. Answers.Where(a => a.Source != AssignmentSource.Proposed)],
    };

    /// <summary>The latest moment anybody decided anything in here.</summary>
    /// <remarks>
    /// A method rather than a property because a decision set is written to a
    /// file, and a computed property would be written with it - a value nothing
    /// reads back, recalculated from the very fields it sits beside.
    /// </remarks>
    /// <remarks>
    /// What a clock check is made against, and deliberately not
    /// <see cref="WrittenUtc"/>. A machine whose clock is a year ahead stamps
    /// the answers as well as the file, and it is the answers that do the harm:
    /// they are what competes on every merge from then on. Judging the file
    /// instead would refuse a machine that has decided nothing, which is a
    /// refusal with nothing behind it, and would let an old set written today
    /// look suspicious for no reason.
    /// </remarks>
    public DateTime LatestDecision() =>
        new[]
        {
            Answers.Count == 0 ? DateTime.MinValue : Answers.Max(a => a.DecidedUtc),
            Strangers.Count == 0 ? DateTime.MinValue : Strangers.Max(s => s.DecidedUtc),
            Turns.Count == 0 ? DateTime.MinValue : Turns.Max(t => t.DecidedUtc),
            Memberships.Count == 0 ? DateTime.MinValue : Memberships.Max(m => m.AddedUtc),
            Rejections.Count == 0 ? DateTime.MinValue : Rejections.Max(r => r.RejectedUtc),
            People.Count == 0 ? DateTime.MinValue : People.Max(Moment),
            Albums.Count == 0 ? DateTime.MinValue : Albums.Max(Moment),
        }.Max();

    private static DateTime Moment(SharedPerson person) =>
        Later(person.UpdatedUtc, person.DeletedUtc);

    private static DateTime Moment(SharedAlbum album) =>
        Later(album.NamedUtc, album.DeletedUtc);

    private static DateTime Later(DateTime? left, DateTime? right) =>
        left is null
            ? right ?? DateTime.MinValue
            : right is null ? left.Value : (left > right ? left.Value : right.Value);
}
