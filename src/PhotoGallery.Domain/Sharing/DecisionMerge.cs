using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Settles what several machines have decided into what this one should do
/// about it.
/// </summary>
/// <remarks>
/// There is no server and no authority. Two machines can disagree, and this has
/// to settle it without asking nine thousand questions.
///
/// <para><strong>Last decision wins, by when it was decided</strong> - which the
/// app very nearly recorded already, because its own convention is a date rather
/// than a flag, adopted so that decisions could be reviewed and undone. Two
/// exceptions, and both are about not trusting a clock further than it deserves:
/// a person's answer never loses to the app's guess whatever the clock says, and
/// a machine whose clock is far enough ahead is refused outright rather than
/// allowed to override everybody for as long as the error lasts.</para>
///
/// <para><strong>Pure, and deliberately so.</strong> Every rule in this feature
/// is about two machines disagreeing, and expressed this way the interesting half
/// is testable with no working folder, no database and no exchange - two decision
/// sets in, a list of changes out. Nothing here reads or writes anything.</para>
///
/// <para><strong>It answers with only what differs.</strong> A merge that would
/// leave the library exactly as it is produces an empty plan, so running it twice
/// changing nothing the second time is a property you can look at rather than a
/// claim somebody has to trust.</para>
/// </remarks>
public static class DecisionMerge
{
    /// <summary>
    /// How far ahead of this machine's clock a decision may be dated before the
    /// whole payload is refused.
    /// </summary>
    /// <remarks>
    /// A day. Laptops on NTP agree to the second, and even a machine with its
    /// time zone set wrongly is out by hours rather than by this - so nothing
    /// correctly set is ever refused. The failure it exists for is a laptop that
    /// sat in a drawer with a flat battery and came back a year ahead: it stamps
    /// every answer it makes into the future and quietly overrides everybody
    /// else's on every merge from then on, including answers made long
    /// afterwards. Nothing about the result looks broken. It just always agrees
    /// with one machine.
    /// </remarks>
    public static readonly TimeSpan FurthestAhead = TimeSpan.FromDays(1);

    /// <summary>
    /// How alike two people's faces must be before they are offered as one
    /// person.
    /// </summary>
    /// <remarks>
    /// Higher than anything else in the app, because this asks a bigger question
    /// than proposing does. Measured on this library: two faces of different
    /// people score around 0.1, two siblings both photographed as babies score
    /// around 0.55, and the same person across a year scores about 0.88. A
    /// proposal at 0.5 is a question worth asking; suggesting that two people are
    /// one person is not, and 0.8 sits above the siblings and below the same
    /// person.
    ///
    /// <para>Offered, never performed. Erring towards silence costs nothing and
    /// erring the other way invites somebody to merge two of their children.</para>
    /// </remarks>
    public const float LooksLikeTheSamePerson = 0.8f;

    /// <summary>
    /// What this machine should change, given what the others have decided.
    /// </summary>
    /// <param name="mine">
    /// Everything this library holds, proposals included. Proposals are not
    /// published - see <see cref="DecisionSet.WithoutProposals"/> - but they are
    /// needed here, because a confirmation arriving from another machine has to
    /// be able to beat one.
    /// </param>
    /// <param name="here">What this machine's own scan has actually indexed.</param>
    /// <param name="nowUtc">This machine's clock, which is what the others are judged against.</param>
    public static MergePlan Merge(
        DecisionSet mine,
        IReadOnlyList<DecisionSet> theirs,
        LibraryContents here,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);
        ArgumentNullException.ThrowIfNull(here);

        (List<DecisionSet> accepted, List<RefusedSet> refused) = Sift(mine, theirs, here, nowUtc);

        if (accepted.Count == 0)
        {
            return MergePlan.Nothing with { Refused = refused };
        }

        List<SharedPerson> people = SettlePeople(mine, accepted);
        Faces faces = SettleFaces(mine, accepted, here);
        (List<PhotoTurn> turns, List<PhotoTurn> heldTurns) = SettleTurns(mine, accepted, here);
        List<SharedAlbum> albums = SettleAlbums(mine, accepted);
        (List<AlbumMove> moves, List<AlbumMembership> heldMoves) =
            SettleMemberships(mine, accepted, here);
        (List<AlbumRejection> rejections, List<AlbumRejection> heldRejections) =
            SettleRejections(mine, accepted, here);

        return new MergePlan(
            people,
            faces.Answers,
            faces.Withdrawn,
            faces.Strangers,
            faces.Recognised,
            turns,
            albums,
            moves,
            rejections,
            SeedEras(mine, accepted),
            new HeldAnswers(
                faces.HeldAnswers,
                faces.HeldStrangers,
                heldTurns,
                heldMoves,
                heldRejections),
            OfferJoins(mine, accepted, people),
            refused);
    }

    /// <summary>
    /// Separates the machines worth listening to from the ones that have to be
    /// reported instead.
    /// </summary>
    private static (List<DecisionSet> Accepted, List<RefusedSet> Refused) Sift(
        DecisionSet mine,
        IReadOnlyList<DecisionSet> theirs,
        LibraryContents here,
        DateTime nowUtc)
    {
        List<DecisionSet> accepted = [];
        List<RefusedSet> refused = [];

        foreach (DecisionSet them in theirs)
        {
            // Our own file, read back out of the shared folder along with
            // everybody else's. Merging it would be harmless and reporting it
            // would be a lie.
            if (them.Machine.Id == mine.Machine.Id)
            {
                continue;
            }

            if (them.Machine.SchemaVersion > mine.Machine.SchemaVersion)
            {
                refused.Add(new RefusedSet(
                    them.Machine,
                    RefusalReason.SchemaTooNew,
                    $"it is running a newer version of Photo Gallery ({them.Machine.AppVersion})"));
                continue;
            }

            TimeSpan ahead = them.LatestDecision() - nowUtc;
            if (ahead > FurthestAhead)
            {
                refused.Add(new RefusedSet(
                    them.Machine,
                    RefusalReason.ClockTooFarAhead,
                    $"its clock is {Roughly(ahead)} ahead of this one"));
                continue;
            }

            if (!them.Sources.Any(here.Sources.Contains))
            {
                refused.Add(new RefusedSet(
                    them.Machine,
                    RefusalReason.NoSourceInCommon,
                    "it has no folder of photographs in common with this library"));
                continue;
            }

            accepted.Add(them);
        }

        return (accepted, refused);
    }

    // ---------------------------------------------------------------- people

    private static List<SharedPerson> SettlePeople(
        DecisionSet mine, List<DecisionSet> accepted)
    {
        Dictionary<Guid, SharedPerson> here = mine.People.ToDictionary(p => p.PublicId);
        Dictionary<Guid, SharedPerson> winners = new(here);

        foreach (SharedPerson person in accepted.SelectMany(them => them.People))
        {
            winners[person.PublicId] =
                winners.TryGetValue(person.PublicId, out SharedPerson? standing)
                    ? Settle(standing, person)
                    : person;
        }

        return
        [
            .. winners.Values.Where(winner =>
                !here.TryGetValue(winner.PublicId, out SharedPerson? ours) || ours != winner),
        ];
    }

    /// <summary>Which of two accounts of one person stands.</summary>
    /// <remarks>
    /// A tombstone always wins, whatever its date. There is no undelete in this
    /// app, so a later rename is not somebody asking for them back - it is
    /// another machine that had not heard yet. Dated at the moment it first
    /// happened rather than the last machine to hear about it.
    /// </remarks>
    private static SharedPerson Settle(SharedPerson mine, SharedPerson theirs)
    {
        SharedPerson named = Named(mine, theirs);
        DateTime? deleted = Earliest(mine.DeletedUtc, theirs.DeletedUtc);

        return named with
        {
            // Somebody's birth year is its own answer and nobody types one twice.
            // Taking the winner's and falling back to the other's keeps it where
            // last-write-wins on the whole row would drop it for a rename.
            BirthYear = named.BirthYear ?? Other(named, mine, theirs).BirthYear,
            DeletedUtc = deleted,
        };
    }

    /// <summary>
    /// Whose name stands: the later rename, and a name nobody has re-typed loses
    /// to one somebody has.
    /// </summary>
    /// <remarks>
    /// Where neither has been re-typed - two libraries that both predate sharing -
    /// there is no moment to compare and the tie is broken on the name itself. It
    /// is arbitrary, and being arbitrary the same way on every machine is the
    /// whole requirement: three machines have to converge without one of them
    /// being first.
    /// </remarks>
    private static SharedPerson Named(SharedPerson mine, SharedPerson theirs)
    {
        int byDate = Nullable.Compare(mine.UpdatedUtc, theirs.UpdatedUtc);

        if (byDate != 0)
        {
            return byDate > 0 ? mine : theirs;
        }

        return string.CompareOrdinal(mine.DisplayName, theirs.DisplayName) >= 0 ? mine : theirs;
    }

    private static SharedPerson Other(SharedPerson chosen, SharedPerson mine, SharedPerson theirs) =>
        ReferenceEquals(chosen, mine) ? theirs : mine;

    // ----------------------------------------------------------------- faces

    /// <summary>What a merge concluded about the faces in this library.</summary>
    private sealed record Faces(
        List<FaceAnswer> Answers,
        List<FaceAnswer> Withdrawn,
        List<StrangerFace> Strangers,
        List<FaceKey> Recognised,
        List<FaceAnswer> HeldAnswers,
        List<StrangerFace> HeldStrangers);

    private static Faces SettleFaces(
        DecisionSet mine, List<DecisionSet> accepted, LibraryContents here)
    {
        Dictionary<(FaceKey Face, Guid Person), FaceAnswer> ours =
            mine.Answers.ToDictionary(a => (a.Face, a.Person));
        Dictionary<FaceKey, StrangerFace> ourStrangers =
            mine.Strangers.ToDictionary(s => s.Face);

        Dictionary<(FaceKey Face, Guid Person), FaceAnswer> answers = new(ours);
        Dictionary<FaceKey, StrangerFace> strangers = new(ourStrangers);
        List<FaceAnswer> heldAnswers = [];
        List<StrangerFace> heldStrangers = [];

        foreach (FaceAnswer answer in accepted.SelectMany(them => them.Answers))
        {
            if (Landed(here, answer.Face) is not FaceKey landed)
            {
                heldAnswers.Add(answer);
                continue;
            }

            FaceAnswer moved = answer with { Face = landed };
            (FaceKey, Guid) key = (landed, answer.Person);

            answers[key] = answers.TryGetValue(key, out FaceAnswer? standing)
                ? Settle(standing, moved)
                : moved;
        }

        foreach (StrangerFace stranger in accepted.SelectMany(them => them.Strangers))
        {
            if (Landed(here, stranger.Face) is not FaceKey landed)
            {
                heldStrangers.Add(stranger);
                continue;
            }

            StrangerFace moved = stranger with { Face = landed };

            strangers[landed] = strangers.TryGetValue(landed, out StrangerFace? standing)
                ? Later(standing, moved)
                : moved;
        }

        return Reconcile(ours, ourStrangers, answers, strangers, heldAnswers, heldStrangers);
    }

    /// <summary>
    /// Decides, face by face, between the people named in it and somebody having
    /// said it is nobody - then keeps only what differs from what is already here.
    /// </summary>
    /// <remarks>
    /// Both are answers a person gave, so the later one stands. Only a
    /// confirmation is in the contest: a rejection or a cleared name says this
    /// face is not one particular person, which does not contradict its being
    /// nobody at all.
    /// </remarks>
    private static Faces Reconcile(
        Dictionary<(FaceKey Face, Guid Person), FaceAnswer> ours,
        Dictionary<FaceKey, StrangerFace> ourStrangers,
        Dictionary<(FaceKey Face, Guid Person), FaceAnswer> answers,
        Dictionary<FaceKey, StrangerFace> strangers,
        List<FaceAnswer> heldAnswers,
        List<StrangerFace> heldStrangers)
    {
        // A face is one person, so of two machines confirming two different
        // people in it only the later answer stands. Everything else is per
        // person and coexists: refusing one name and confirming another, or
        // clearing one and rejecting a second, are all true of the same face at
        // the same time.
        Dictionary<FaceKey, FaceAnswer> named = [];
        foreach (FaceAnswer answer in answers.Values
            .Where(answer => answer.Source == AssignmentSource.Confirmed))
        {
            named[answer.Face] =
                named.TryGetValue(answer.Face, out FaceAnswer? standing)
                && Wins(standing.DecidedUtc, standing.DecidedBy, answer.DecidedUtc, answer.DecidedBy)
                    ? standing
                    : answer;
        }

        foreach ((FaceKey face, Guid person) key in answers.Keys.ToList())
        {
            if (answers[key].Source == AssignmentSource.Confirmed
                && named[key.face].Person != key.person)
            {
                answers.Remove(key);
            }
        }

        HashSet<FaceKey> nobody = [];

        foreach ((FaceKey face, StrangerFace stranger) in strangers)
        {
            if (stranger.DecidedUtc
                >= (named.TryGetValue(face, out FaceAnswer? who) ? who.DecidedUtc : DateTime.MinValue))
            {
                nobody.Add(face);
            }
        }

        return new Faces(
            [
                .. answers
                    .Where(entry => !nobody.Contains(entry.Key.Face))
                    .Where(entry => !ours.TryGetValue(entry.Key, out FaceAnswer? was)
                                 || was != entry.Value)
                    .Select(entry => entry.Value),
            ],

            // Rows this library holds that no longer stand: a name it had
            // confirmed on a face somebody else has since said is somebody
            // different, or is nobody at all. Not published - every machine
            // reaches the same conclusion from the same answers - but the row
            // has to go, or the face ends up two people.
            [
                .. ours
                    .Where(entry => !answers.ContainsKey(entry.Key)
                                 || nobody.Contains(entry.Key.Face))
                    .Where(entry => entry.Value.Source == AssignmentSource.Confirmed)
                    .Select(entry => entry.Value),
            ],
            [
                .. nobody
                    .Where(face => !ourStrangers.ContainsKey(face))
                    .Select(face => strangers[face]),
            ],

            // A face this library had set aside that somebody has since named. The
            // mark has to come off, or the name lands on a face nothing will show.
            [.. ourStrangers.Keys.Where(face => !nobody.Contains(face))],
            heldAnswers,
            heldStrangers);
    }

    /// <summary>
    /// The face on this machine that another machine's box is talking about, or
    /// null when this machine has not found it yet.
    /// </summary>
    /// <remarks>
    /// Null covers three situations that all want the same outcome: a photograph
    /// this library has not indexed, one indexed but not yet looked at for faces,
    /// and one whose faces do not include this box at all. In every case the
    /// answer waits.
    /// </remarks>
    private static FaceKey? Landed(LibraryContents here, FaceKey wanted)
    {
        if (!here.Faces.TryGetValue(wanted.Photo, out IReadOnlyList<FaceBounds>? boxes))
        {
            return null;
        }

        return FaceMatching.Find(boxes, wanted.Bounds) is FaceBounds found
            ? new FaceKey(wanted.Photo, found)
            : null;
    }

    /// <summary>Which of two answers about one face and one person stands.</summary>
    /// <remarks>
    /// A person's answer never loses to the app's guess, whatever the clock says.
    /// Clocks are the weak part of last-write-wins - close enough on NTP-synced
    /// laptops for two human answers minutes apart, and not something to bet a
    /// confirmed name on against a proposal that happened to be written later.
    /// </remarks>
    private static FaceAnswer Settle(FaceAnswer mine, FaceAnswer theirs)
    {
        bool mineIsHuman = mine.Source != AssignmentSource.Proposed;
        bool theirsIsHuman = theirs.Source != AssignmentSource.Proposed;

        if (mineIsHuman != theirsIsHuman)
        {
            return mineIsHuman ? mine : theirs;
        }

        return Wins(mine.DecidedUtc, mine.DecidedBy, theirs.DecidedUtc, theirs.DecidedBy)
            ? mine
            : theirs;
    }

    private static StrangerFace Later(StrangerFace mine, StrangerFace theirs) =>
        Wins(mine.DecidedUtc, mine.DecidedBy, theirs.DecidedUtc, theirs.DecidedBy)
            ? mine
            : theirs;

    // ----------------------------------------------------------------- turns

    private static (List<PhotoTurn> Applied, List<PhotoTurn> Held) SettleTurns(
        DecisionSet mine, List<DecisionSet> accepted, LibraryContents here)
    {
        Dictionary<AssetKey, PhotoTurn> ours = mine.Turns.ToDictionary(t => t.Photo);
        Dictionary<AssetKey, PhotoTurn> winners = new(ours);
        List<PhotoTurn> held = [];

        foreach (PhotoTurn turn in accepted.SelectMany(them => them.Turns))
        {
            if (!here.Photographs.Contains(turn.Photo))
            {
                held.Add(turn);
                continue;
            }

            winners[turn.Photo] = winners.TryGetValue(turn.Photo, out PhotoTurn? standing)
                && Wins(standing.DecidedUtc, standing.DecidedBy, turn.DecidedUtc, turn.DecidedBy)
                    ? standing
                    : turn;
        }

        // A photograph nobody here has turned is upright as far as this library is
        // concerned, so a turn of nothing from another machine changes nothing.
        return (
            [
                .. winners.Values.Where(winner =>
                    winner.Rotation != (ours.TryGetValue(winner.Photo, out PhotoTurn? was)
                        ? was.Rotation
                        : 0)),
            ],
            held);
    }

    // ---------------------------------------------------------------- albums

    private static List<SharedAlbum> SettleAlbums(DecisionSet mine, List<DecisionSet> accepted)
    {
        Dictionary<string, SharedAlbum> ours = mine.Albums.ToDictionary(Identity);
        Dictionary<string, SharedAlbum> winners = new(ours);

        foreach (SharedAlbum album in accepted.SelectMany(them => them.Albums))
        {
            // A proposal nobody has renamed or thrown away carries no decision at
            // all. The other machine makes its own from the same photographs, and
            // better ones once it has the confirmations that came with this.
            if (album.Origin == CollectionOrigin.Proposed
                && album.NamedUtc is null
                && album.DeletedUtc is null)
            {
                continue;
            }

            string identity = Identity(album);

            winners[identity] = winners.TryGetValue(identity, out SharedAlbum? standing)
                ? Settle(standing, album)
                : album;
        }

        return
        [
            .. winners.Values.Where(winner =>
                !ours.TryGetValue(Identity(winner), out SharedAlbum? was) || was != winner),
        ];
    }

    /// <summary>
    /// What names an album across machines: its run of days where it has one, and
    /// its identity otherwise.
    /// </summary>
    /// <remarks>
    /// A proposed row is derived - the pass deletes and reinserts it, so its
    /// identity changes for reasons that have nothing to do with the user - and
    /// the span of days is what survives that. An album somebody made has no span
    /// and is only ever itself.
    /// </remarks>
    private static string Identity(SharedAlbum album) =>
        album.ProposalKey is null ? $"id:{album.PublicId:D}" : $"days:{album.ProposalKey}";

    private static SharedAlbum Settle(SharedAlbum mine, SharedAlbum theirs)
    {
        int byDate = Nullable.Compare(mine.NamedUtc, theirs.NamedUtc);

        SharedAlbum named = byDate != 0
            ? (byDate > 0 ? mine : theirs)
            : string.CompareOrdinal(mine.Name, theirs.Name) >= 0 ? mine : theirs;

        return named with { DeletedUtc = Earliest(mine.DeletedUtc, theirs.DeletedUtc) };
    }

    private static (List<AlbumMove> Moves, List<AlbumMembership> Held) SettleMemberships(
        DecisionSet mine, List<DecisionSet> accepted, LibraryContents here)
    {
        Dictionary<AssetKey, AlbumMembership> ours = mine.Memberships.ToDictionary(m => m.Photo);
        Dictionary<AssetKey, AlbumMembership> winners = new(ours);
        List<AlbumMembership> held = [];

        foreach (AlbumMembership membership in accepted.SelectMany(them => them.Memberships))
        {
            if (!here.Photographs.Contains(membership.Photo))
            {
                held.Add(membership);
                continue;
            }

            winners[membership.Photo] =
                winners.TryGetValue(membership.Photo, out AlbumMembership? standing)
                && Wins(standing.AddedUtc, standing.DecidedBy, membership.AddedUtc, membership.DecidedBy)
                    ? standing
                    : membership;
        }

        return (
            [
                .. winners.Values
                    .Select(winner => new
                    {
                        Winner = winner,
                        Was = ours.TryGetValue(winner.Photo, out AlbumMembership? was) ? was : null,
                    })
                    .Where(move => move.Was?.Album != move.Winner.Album)
                    .Select(move => new AlbumMove(
                        move.Winner.Photo,
                        move.Was?.Album,
                        move.Winner.Album,
                        move.Winner.AddedUtc,
                        move.Winner.DecidedBy)),
            ],
            held);
    }

    private static (List<AlbumRejection> Applied, List<AlbumRejection> Held) SettleRejections(
        DecisionSet mine, List<DecisionSet> accepted, LibraryContents here)
    {
        HashSet<(AssetKey Photo, string Key)> ours =
            [.. mine.Rejections.Select(r => (r.Photo, r.ProposalKey))];

        List<AlbumRejection> applied = [];
        List<AlbumRejection> held = [];
        HashSet<(AssetKey, string)> seen = [];

        // Rejections only ever accumulate, so two machines never disagree about
        // one: the merge is a union rather than a contest.
        foreach (AlbumRejection rejection in accepted.SelectMany(them => them.Rejections))
        {
            (AssetKey Photo, string ProposalKey) key = (rejection.Photo, rejection.ProposalKey);

            if (ours.Contains(key) || !seen.Add(key))
            {
                continue;
            }

            if (here.Photographs.Contains(rejection.Photo))
            {
                applied.Add(rejection);
            }
            else
            {
                held.Add(rejection);
            }
        }

        return (applied, held);
    }

    // ------------------------------------------------------------------ eras

    /// <summary>
    /// The centroids worth keeping: the ones this library cannot build for
    /// itself.
    /// </summary>
    /// <remarks>
    /// The one deliberate exception to sending only what cannot be re-derived,
    /// and it stays an exception by being a seed rather than a fact. Where this
    /// machine has its own era covering that stretch, its own confirmed faces
    /// made it and they are worth more than an average of somebody else's.
    /// </remarks>
    private static List<SharedEra> SeedEras(DecisionSet mine, List<DecisionSet> accepted)
    {
        List<SharedEra> ours = [.. mine.Eras];
        List<SharedEra> seeds = [];

        foreach (SharedEra era in accepted.SelectMany(them => them.Eras))
        {
            // Once per person and stretch, however many machines offer one.
            // Taking them all would give somebody several centroids covering the
            // same years, which is not a richer answer - it is the same answer
            // three times, and the next rebuild would have to choose between
            // them for no reason.
            if (ours.Concat(seeds).Any(held => held.Person == era.Person && Overlaps(held, era)))
            {
                continue;
            }

            seeds.Add(era);
        }

        return seeds;
    }

    private static bool Overlaps(SharedEra left, SharedEra right) =>
        left.FromUtc < right.ToUtc && right.FromUtc < left.ToUtc;

    // ----------------------------------------------------------------- joins

    /// <summary>
    /// Two people who might be one, put on screen with their faces rather than
    /// joined.
    /// </summary>
    private static List<PersonJoin> OfferJoins(
        DecisionSet mine, List<DecisionSet> accepted, List<SharedPerson> changed)
    {
        Dictionary<Guid, SharedPerson> everybody = mine.People.ToDictionary(p => p.PublicId);
        foreach (SharedPerson person in changed)
        {
            everybody[person.PublicId] = person;
        }

        List<SharedPerson> living = [.. everybody.Values.Where(p => p.DeletedUtc is null)];
        Dictionary<Guid, List<SharedEra>> eras = ErasByPerson(mine, accepted);
        List<PersonJoin> joins = [];

        for (int i = 0; i < living.Count; i++)
        {
            for (int j = i + 1; j < living.Count; j++)
            {
                SharedPerson left = living[i];
                SharedPerson right = living[j];

                bool sameName = string.Equals(
                    left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);

                float alike = Alike(
                    eras.GetValueOrDefault(left.PublicId, []),
                    eras.GetValueOrDefault(right.PublicId, []));

                bool lookAlike = alike >= LooksLikeTheSamePerson;

                if (!sameName && !lookAlike)
                {
                    continue;
                }

                joins.Add(new PersonJoin(
                    left.PublicId,
                    right.PublicId,
                    sameName && lookAlike
                        ? JoinEvidence.Both
                        : sameName ? JoinEvidence.SameName : JoinEvidence.TheyLookAlike,
                    alike));
            }
        }

        return joins;
    }

    private static Dictionary<Guid, List<SharedEra>> ErasByPerson(
        DecisionSet mine, List<DecisionSet> accepted)
    {
        Dictionary<Guid, List<SharedEra>> eras = [];

        foreach (SharedEra era in mine.Eras.Concat(accepted.SelectMany(them => them.Eras)))
        {
            if (!eras.TryGetValue(era.Person, out List<SharedEra>? theirs))
            {
                eras[era.Person] = theirs = [];
            }

            theirs.Add(era);
        }

        return eras;
    }

    /// <summary>
    /// How alike two people are, comparing only the stretches of time they both
    /// cover.
    /// </summary>
    /// <remarks>
    /// Eras exist because a face changes more across childhood than most adults
    /// differ from each other, so comparing somebody's baby era against somebody
    /// else's adult one answers a question nobody asked. Where two people have no
    /// overlapping era there is nothing to compare and the answer is no.
    /// </remarks>
    private static float Alike(List<SharedEra> left, List<SharedEra> right)
    {
        float best = 0f;

        foreach (SharedEra one in left)
        {
            foreach (SharedEra other in right)
            {
                if (!Overlaps(one, other)
                    || one.Centroid.IsEmpty
                    || other.Centroid.IsEmpty)
                {
                    continue;
                }

                best = Math.Max(best, one.Centroid.SimilarityTo(other.Centroid));
            }
        }

        return best;
    }

    // ----------------------------------------------------------------- shared

    /// <summary>
    /// Whether the standing answer keeps its place, by date and then by machine.
    /// </summary>
    /// <remarks>
    /// The machine is not a tie-break for tidiness. Two answers stamped the same
    /// second have to be settled the same way on every laptop in the house, or
    /// three machines never converge - each one keeps whichever it happened to
    /// read last, and every merge undoes the one before.
    /// </remarks>
    private static bool Wins(DateTime mine, Guid mineBy, DateTime theirs, Guid theirsBy) =>
        mine != theirs ? mine > theirs : mineBy.CompareTo(theirsBy) >= 0;

    private static DateTime? Earliest(DateTime? left, DateTime? right) =>
        left is null ? right
        : right is null ? left
        : left < right ? left : right;

    /// <summary>How far ahead a clock is, in words somebody can act on.</summary>
    /// <remarks>
    /// Rounded on purpose. The number is not the point - what the user has to do
    /// is go and fix the date on that machine, and "about a year" says that more
    /// plainly than 374 days.
    /// </remarks>
    private static string Roughly(TimeSpan ahead) => ahead.TotalDays switch
    {
        >= 365 => Many(ahead.TotalDays / 365, "year"),
        >= 60 => Many(ahead.TotalDays / 30, "month"),
        >= 2 => Many(ahead.TotalDays, "day"),
        _ => Many(ahead.TotalHours, "hour"),
    };

    private static string Many(double count, string unit)
    {
        long whole = (long)Math.Round(count);
        return whole == 1 ? $"1 {unit}" : $"{whole:N0} {unit}s";
    }
}
