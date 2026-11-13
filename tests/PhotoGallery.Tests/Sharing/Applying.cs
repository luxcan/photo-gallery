using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Carries out a merge plan against a decision set, so that "merging twice
/// changes nothing" can be asserted rather than assumed.
/// </summary>
/// <remarks>
/// Idempotence tested against a hand-written "and afterwards it looks like
/// this" is not tested at all - it asserts what the person writing the test
/// believed the plan would do. This applies the plan the merge actually
/// produced, which is the only version of that claim worth making.
///
/// <para>Deliberately in the tests rather than in the domain. Applying a plan
/// for real means rows, files and a scan phase, and none of that belongs beside
/// a pure function; what belongs here is only enough of it to close the loop.
/// It is also a useful shape to have argued out before the handler that does it
/// against a database exists.</para>
/// </remarks>
internal static class Applying
{
    public static DecisionSet Apply(this DecisionSet mine, MergePlan plan)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(plan);

        // A kept album that a later dismissal beat is deleted outright, and its
        // photographs go with it - the database cascades them.
        HashSet<string> dismissed = [.. plan.DismissedHere.Select(d => $"days:{d.ProposalKey}")];
        HashSet<Guid> gone =
        [
            .. mine.Albums.Where(album => dismissed.Contains(Identity(album)))
                .Select(album => album.PublicId),
        ];

        DecisionSet kept = mine with
        {
            Albums = [.. mine.Albums.Where(album => !gone.Contains(album.PublicId))],
            Memberships = [.. mine.Memberships.Where(membership => !gone.Contains(membership.Album))],
        };

        return kept with
        {
            People = Upsert(kept.People, plan.People, person => person.PublicId),
            Answers = Answers(kept, plan),
            Strangers = Strangers(kept, plan),
            Turns = Upsert(kept.Turns, plan.Turns, turn => turn.Photo),
            Albums = Albums(kept, plan),
            Memberships = Memberships(kept, plan),
            Rejections = [.. kept.Rejections, .. plan.Rejections],
            Eras = [.. kept.Eras, .. plan.Eras],
            Dismissals = Upsert(kept.Dismissals, plan.Dismissals, d => d.ProposalKey),
        };
    }

    /// <summary>
    /// The albums afterwards, each row keeping the identity it already had.
    /// </summary>
    /// <remarks>
    /// The write matches a row by its run of days and never renumbers it, so a
    /// suggestion this library adopts keeps its own identity whatever the album
    /// that won was called on the machine that kept it. Taking the winner's
    /// identity here instead would let a second merge pass for the wrong
    /// reason.
    /// </remarks>
    private static IReadOnlyList<SharedAlbum> Albums(DecisionSet mine, MergePlan plan)
    {
        Dictionary<string, SharedAlbum> byIdentity = [];
        foreach (SharedAlbum album in mine.Albums)
        {
            byIdentity[Identity(album)] = album;
        }

        foreach (SharedAlbum settled in plan.Albums)
        {
            byIdentity[Identity(settled)] =
                byIdentity.TryGetValue(Identity(settled), out SharedAlbum? row)
                    ? settled with { PublicId = row.PublicId }
                    : settled;
        }

        return [.. byIdentity.Values];
    }

    private static IReadOnlyList<FaceAnswer> Answers(DecisionSet mine, MergePlan plan)
    {
        // A face that has become nobody keeps no names, exactly as marking one
        // aside does locally: anything said about a face that is nobody was said
        // about the wrong thing, so it goes with it.
        HashSet<FaceKey> nobody = [.. plan.Strangers.Select(s => s.Face)];
        HashSet<(FaceKey, Guid)> withdrawn = [.. plan.Withdrawn.Select(a => (a.Face, a.Person))];

        List<FaceAnswer> kept =
        [
            .. mine.Answers.Where(answer =>
                !nobody.Contains(answer.Face) && !withdrawn.Contains((answer.Face, answer.Person))),
        ];

        return Upsert(kept, plan.Answers, answer => (answer.Face, answer.Person));
    }

    private static IReadOnlyList<StrangerFace> Strangers(DecisionSet mine, MergePlan plan)
    {
        HashSet<FaceKey> recognised = [.. plan.Recognised];

        return Upsert(
            [.. mine.Strangers.Where(s => !recognised.Contains(s.Face))],
            plan.Strangers,
            stranger => stranger.Face);
    }

    private static IReadOnlyList<SharedAlbumMembership> Memberships(DecisionSet mine, MergePlan plan)
    {
        HashSet<AssetKey> moved = [.. plan.Moves.Select(move => move.Photo)];

        return
        [
            .. mine.Memberships.Where(membership => !moved.Contains(membership.Photo)),
            .. plan.Moves
                .Where(move => move.To is not null)
                .Select(move => new SharedAlbumMembership(
                    move.Photo, move.To!.Value, move.AddedUtc, move.DecidedBy)),
        ];
    }

    private static string Identity(SharedAlbum album) =>
        album.ProposalKey is null ? $"id:{album.PublicId:D}" : $"days:{album.ProposalKey}";

    private static IReadOnlyList<T> Upsert<T, TKey>(
        IReadOnlyList<T> mine, IReadOnlyList<T> arriving, Func<T, TKey> keyOf)
        where TKey : notnull
    {
        Dictionary<TKey, T> byKey = [];
        foreach (T item in mine.Concat(arriving))
        {
            byKey[keyOf(item)] = item;
        }

        return [.. byKey.Values];
    }
}
