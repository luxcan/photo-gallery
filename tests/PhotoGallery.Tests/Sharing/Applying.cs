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

        return mine with
        {
            People = Upsert(mine.People, plan.People, person => person.PublicId),
            Answers = Answers(mine, plan),
            Strangers = Strangers(mine, plan),
            Turns = Upsert(mine.Turns, plan.Turns, turn => turn.Photo),
            Albums = Upsert(mine.Albums, plan.Albums, Identity),
            Memberships = Memberships(mine, plan),
            Rejections = [.. mine.Rejections, .. plan.Rejections],
            Eras = [.. mine.Eras, .. plan.Eras],
        };
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
            .. plan.Moves.Select(move => new SharedAlbumMembership(
                move.Photo, move.To, move.AddedUtc, move.DecidedBy)),
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
