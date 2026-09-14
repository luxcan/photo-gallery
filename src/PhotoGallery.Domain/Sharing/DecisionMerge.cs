using PhotoGallery.Domain.Albums;
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

        // Folders first, and before anything else looks at a key, because a link
        // changes what every key below it means.
        List<SourceLink> links = SettleLinks(mine, theirs);
        IReadOnlyDictionary<Guid, Guid> renames =
            SourcePairing.Adopt([.. mine.Sources.Select(source => source.SharedId)], links);

        // Proposed from the sets as they were written, so a folder still shows
        // the name its own machine gives it.
        List<PairingProposal> pairings = Pairings(mine, theirs, links);

        // Everything read in one identity from here down. A pairing confirmed on
        // this laptop has to work on this laptop, now - waiting for the other
        // machine to merge, rename and republish would make a confirmation take
        // three shares to come true, with nothing on screen to say why the first
        // two did nothing.
        mine = Translate(mine, links);
        here = Translate(here, links);
        List<DecisionSet> spoken = [.. theirs.Select(them => Translate(them, links))];

        (List<DecisionSet> accepted, List<RefusedSet> refused) =
            Sift(mine, spoken, here, nowUtc);

        if (accepted.Count == 0)
        {
            return MergePlan.Nothing with
            {
                Refused = refused,
                Links = links,
                Renames = renames,
                Pairings = pairings,
            };
        }

        List<SharedPerson> people = SettlePeople(mine, accepted);
        Faces faces = SettleFaces(mine, accepted, here);
        (List<PhotoTurn> turns, List<PhotoTurn> heldTurns) = SettleTurns(mine, accepted, here);
        List<SharedAlbum> albums = SettleAlbums(mine, accepted, here);
        (List<SharedAlbumMove> moves, List<SharedAlbumMembership> heldMoves) =
            SettleMemberships(mine, accepted, here, albums);
        (List<SharedAlbumRejection> rejections, List<SharedAlbumRejection> heldRejections) =
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
            SettleCollections(mine, accepted),
            new HeldAnswers(
                faces.HeldAnswers,
                faces.HeldStrangers,
                heldTurns,
                heldMoves,
                heldRejections),
            OfferJoins(mine, accepted, people),
            refused,
            links,
            renames,
            pairings);
    }

    /// <summary>
    /// Settles answers that have been waiting for their photographs against a
    /// library that has just indexed some more of them.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="Merge"/>, and what makes the order of
    /// operations impossible to get wrong. An answer about a photograph this
    /// library had not indexed is parked rather than dropped; this is what
    /// brings it back the moment a scan finds the picture it was about.
    ///
    /// <para><strong>Nothing is sifted here.</strong> These answers were sifted
    /// when they arrived - the machine that sent them was judged then, on its
    /// schema version, its clock and the sources it had in common. Asking those
    /// questions a second time would refuse an answer against a machine that is
    /// no longer in the room, and a held answer refused is one that nothing
    /// would ever explain to anybody.</para>
    ///
    /// <para>Everything after that is the ordinary settling, on purpose. Two
    /// machines can have named the same face while it waited, and a photograph
    /// can have been set aside on one and named on another. Those are the
    /// disagreements <see cref="Merge"/> exists to settle, arriving late. What
    /// still cannot land stays held, which is why a photograph indexed but not
    /// yet looked at for faces keeps its names for the pass that finds
    /// them.</para>
    /// </remarks>
    /// <param name="mine">What this library holds, proposals included.</param>
    /// <param name="waiting">The answers that have been parked.</param>
    /// <param name="here">What this machine's scan has indexed by now.</param>
    public static MergePlan Rejoin(DecisionSet mine, HeldAnswers waiting, LibraryContents here)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(here);

        if (waiting.Count == 0)
        {
            return MergePlan.Nothing;
        }

        // The identity on this set is never read. Sifting is the only thing that
        // looks at whose set it is, and these have been through it already;
        // every answer inside carries its own author and its own moment, which
        // is what settles it against a competing one.
        List<DecisionSet> parked =
        [
            DecisionSet.Empty(mine.Machine, mine.WrittenUtc) with
            {
                Answers = waiting.Answers,
                Strangers = waiting.Strangers,
                Turns = waiting.Turns,
                Memberships = waiting.Memberships,
                Rejections = waiting.Rejections,
            },
        ];

        Faces faces = SettleFaces(mine, parked, here);
        (List<PhotoTurn> turns, List<PhotoTurn> heldTurns) = SettleTurns(mine, parked, here);
        (List<SharedAlbumMove> moves, List<SharedAlbumMembership> heldMoves) =
            SettleMemberships(mine, parked, here, []);
        (List<SharedAlbumRejection> rejections, List<SharedAlbumRejection> heldRejections) =
            SettleRejections(mine, parked, here);

        // No people, albums or eras. Those are not about a photograph, so they
        // were never held: they landed on the merge that carried them.
        return MergePlan.Nothing with
        {
            Answers = faces.Answers,
            Withdrawn = faces.Withdrawn,
            Strangers = faces.Strangers,
            Recognised = faces.Recognised,
            Turns = turns,
            Moves = moves,
            Rejections = rejections,
            Held = new HeldAnswers(
                faces.HeldAnswers,
                faces.HeldStrangers,
                heldTurns,
                heldMoves,
                heldRejections),
        };
    }

    /// <summary>
    /// The same decisions, with every paired folder written as the one identity
    /// the links settle on.
    /// </summary>
    /// <remarks>
    /// The whole of what a pairing does. Two machines reaching one share by a
    /// UNC path and a mapped drive letter each key their answers on an id the
    /// other has never seen; a link says the two are one, and this is where that
    /// stops being a fact and starts being true of the keys.
    /// </remarks>
    private static DecisionSet Translate(DecisionSet set, List<SourceLink> links)
    {
        if (links.Count == 0)
        {
            return set;
        }

        IReadOnlyDictionary<Guid, Guid> map = SourcePairing.Adopt(
            [.. Everywhere(set)], links);

        return map.Count == 0
            ? set
            : set with
            {
                Sources =
                [
                    .. set.Sources.Select(source => source with
                    {
                        SharedId = As(map, source.SharedId),
                    }),
                ],
                Answers = [.. set.Answers.Select(a => a with { Face = As(map, a.Face) })],
                Strangers = [.. set.Strangers.Select(s => s with { Face = As(map, s.Face) })],
                Turns = [.. set.Turns.Select(t => t with { Photo = As(map, t.Photo) })],
                Memberships = [.. set.Memberships.Select(m => m with { Photo = As(map, m.Photo) })],
                Rejections = [.. set.Rejections.Select(r => r with { Photo = As(map, r.Photo) })],
            };
    }

    /// <summary>
    /// What this library holds, in the same identity its answers are read in.
    /// </summary>
    /// <remarks>
    /// Translated as well as the decisions, and it has to be: whether an answer
    /// lands or waits is decided by looking the photograph up in here, so a
    /// contents left in the old identity would hold every incoming answer for a
    /// picture sitting right there.
    /// </remarks>
    private static LibraryContents Translate(LibraryContents here, List<SourceLink> links)
    {
        if (links.Count == 0)
        {
            return here;
        }

        IReadOnlyDictionary<Guid, Guid> map = SourcePairing.Adopt([.. here.Sources], links);

        if (map.Count == 0)
        {
            return here;
        }

        Dictionary<AssetKey, IReadOnlyList<FaceBounds>> faces = [];
        foreach ((AssetKey photo, IReadOnlyList<FaceBounds> boxes) in here.Faces)
        {
            faces[As(map, photo)] = boxes;
        }

        return here with
        {
            Sources = new HashSet<Guid>(here.Sources.Select(source => As(map, source))),
            Photographs = new HashSet<AssetKey>(here.Photographs.Select(photo => As(map, photo))),
            Faces = faces,
        };
    }

    /// <summary>Every shared id a set mentions, in its keys as well as its roots.</summary>
    private static HashSet<Guid> Everywhere(DecisionSet set) =>
        [
            .. set.Sources.Select(source => source.SharedId),
            .. set.Answers.Select(a => a.Face.Photo.SharedSourceId),
            .. set.Strangers.Select(s => s.Face.Photo.SharedSourceId),
            .. set.Turns.Select(t => t.Photo.SharedSourceId),
            .. set.Memberships.Select(m => m.Photo.SharedSourceId),
            .. set.Rejections.Select(r => r.Photo.SharedSourceId),
        ];

    private static Guid As(IReadOnlyDictionary<Guid, Guid> map, Guid source) =>
        map.TryGetValue(source, out Guid canonical) ? canonical : source;

    private static AssetKey As(IReadOnlyDictionary<Guid, Guid> map, AssetKey photo) =>
        map.TryGetValue(photo.SharedSourceId, out Guid canonical)
            ? new AssetKey(canonical, photo.RelativePath)
            : photo;

    private static FaceKey As(IReadOnlyDictionary<Guid, Guid> map, FaceKey face) =>
        map.ContainsKey(face.Photo.SharedSourceId)
            ? new FaceKey(As(map, face.Photo), face.Bounds)
            : face;

    /// <summary>
    /// Every pairing anybody has confirmed, this library's own included.
    /// </summary>
    /// <remarks>
    /// A union rather than a contest. Two machines never disagree about a link:
    /// saying two folders are one is not a claim that competes with anything,
    /// and there is deliberately no way to say they are not - unpairing would be
    /// a decision that had to travel further than the pairing it undid, and
    /// nothing in this house needs it.
    /// </remarks>
    private static List<SourceLink> SettleLinks(DecisionSet mine, IReadOnlyList<DecisionSet> theirs)
    {
        Dictionary<(Guid, Guid), SourceLink> links = [];

        foreach (SourceLink link in mine.Links.Concat(theirs.SelectMany(them => them.Links)))
        {
            SourceLink ordered = link.Ordered();
            (Guid, Guid) key = (ordered.Left, ordered.Right);

            // The earliest, so that a link forwarded round the house keeps the
            // moment it was actually made rather than the moment it arrived.
            if (!links.TryGetValue(key, out SourceLink? standing)
                || ordered.PairedUtc < standing.PairedUtc)
            {
                links[key] = ordered;
            }
        }

        return [.. links.Values];
    }

    /// <summary>Folders worth asking a person about, across every machine.</summary>
    private static List<PairingProposal> Pairings(
        DecisionSet mine, IReadOnlyList<DecisionSet> theirs, List<SourceLink> links)
    {
        List<PairingProposal> proposals = [];

        foreach (DecisionSet them in theirs)
        {
            if (them.Machine.Id == mine.Machine.Id)
            {
                continue;
            }

            proposals.AddRange(SourcePairing.Propose(
                mine.Sources, them.Sources, them.Machine.Name, links));
        }

        return proposals;
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

            if (!them.Sources.Any(source => here.Sources.Contains(source.SharedId)))
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

    /// <param name="here">
    /// What this machine has actually indexed, which is what decides whether a
    /// cover can be taken. A cover names a photograph, so it is the one thing
    /// about an album that can arrive before its picture does.
    /// </param>
    private static List<SharedAlbum> SettleAlbums(
        DecisionSet mine, List<DecisionSet> accepted, LibraryContents here)
    {
        Dictionary<string, SharedAlbum> ours = mine.Albums.ToDictionary(Identity);
        Dictionary<string, SharedAlbum> winners = new(ours);

        foreach (SharedAlbum album in accepted.SelectMany(them => them.Albums))
        {
            // A proposal nobody has renamed or thrown away carries no decision at
            // all. The other machine makes its own from the same photographs, and
            // better ones once it has the confirmations that came with this.
            if (album.Origin == AlbumOrigin.Proposed
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
            .. winners.Values
                .Select(winner => Keepable(winner, ours, here))
                .Where(winner =>
                    !ours.TryGetValue(Identity(winner), out SharedAlbum? was) || was != winner),
        ];
    }

    /// <summary>
    /// The same album with a cover this library cannot honour put back to the
    /// one it already had.
    /// </summary>
    /// <remarks>
    /// A cover naming a photograph this machine has not indexed is not refused
    /// and is not held: it is simply not taken yet. Decision sets are whole
    /// state rather than a log, so the machine that chose it goes on saying so,
    /// and the choice lands by itself on the first merge after the scan that
    /// finds the picture. Holding it would buy one thing - surviving that
    /// machine later forgetting the photograph - at the price of a fifth kind of
    /// waiting answer, a count on the Sharing screen and a second way for the
    /// same fact to arrive.
    ///
    /// <para>Put back rather than dropped, because the plan is a difference: a
    /// winner carrying a cover that will not be written is an album that differs
    /// from this library's for ever, and "merging twice changes nothing" would
    /// quietly stop being true on the album nobody could see the change in.</para>
    /// </remarks>
    private static SharedAlbum Keepable(
        SharedAlbum winner, Dictionary<string, SharedAlbum> ours, LibraryContents here)
    {
        if (winner.Cover is not AssetKey cover || here.Photographs.Contains(cover))
        {
            return winner;
        }

        SharedAlbum? was = ours.TryGetValue(Identity(winner), out SharedAlbum? mine) ? mine : null;

        return winner with { Cover = was?.Cover, CoverChosenUtc = was?.CoverChosenUtc };
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

        // The shelf is settled on its own date, not carried by whichever side
        // won the name. They are two decisions: somebody renaming an album on
        // one laptop must not silently undo somebody else moving it on another,
        // and one date for both is exactly how that would happen.
        SharedAlbum shelved = Shelf(mine, theirs);

        // And the cover on its own date again, for the third time and the third
        // reason: which picture an album shows is not an opinion about its name
        // or its shelf, and somebody renaming it must not quietly replace a
        // photograph somebody else chose.
        SharedAlbum covered = Cover(mine, theirs);

        return named with
        {
            DeletedUtc = Earliest(mine.DeletedUtc, theirs.DeletedUtc),
            Shelf = shelved.Shelf,
            ShelvedUtc = shelved.ShelvedUtc,
            Cover = covered.Cover,
            CoverChosenUtc = covered.CoverChosenUtc,
        };
    }

    /// <summary>
    /// Which of two machines last chose the picture an album shows.
    /// </summary>
    /// <remarks>
    /// A null date is a library where nobody has chosen one and the app is still
    /// working it out, and it loses to any date - because a guess losing to an
    /// answer is the whole of what this settles. The tie-break is on the key's
    /// own text, so two machines settling the same instant land on the same
    /// photograph without either being asked.
    /// </remarks>
    private static SharedAlbum Cover(SharedAlbum mine, SharedAlbum theirs)
    {
        int byDate = Nullable.Compare(mine.CoverChosenUtc, theirs.CoverChosenUtc);

        if (byDate != 0)
        {
            return byDate > 0 ? mine : theirs;
        }

        return string.CompareOrdinal(Text(mine.Cover), Text(theirs.Cover)) >= 0 ? mine : theirs;
    }

    private static string Text(AssetKey? cover) => cover?.ToString() ?? string.Empty;

    /// <summary>
    /// Which of two machines last said where an album sits.
    /// </summary>
    /// <remarks>
    /// A null date is a library that has never shelved this album, and it loses
    /// to any date - including to a machine that took the album off a shelf,
    /// which is a decision with a moment behind it rather than an absence. The
    /// tie-break is on the shelf's own identity so that two machines settling the
    /// same instant land on the same answer without either being asked.
    /// </remarks>
    private static SharedAlbum Shelf(SharedAlbum mine, SharedAlbum theirs)
    {
        int byDate = Nullable.Compare(mine.ShelvedUtc, theirs.ShelvedUtc);

        if (byDate != 0)
        {
            return byDate > 0 ? mine : theirs;
        }

        return Compare(mine.Shelf, theirs.Shelf) >= 0 ? mine : theirs;
    }

    private static int Compare(Guid? mine, Guid? theirs) => (mine, theirs) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        var (ours, them) => ours.Value.CompareTo(them!.Value),
    };

    /// <summary>
    /// The shelves, settled the way everything else with a name and a tombstone
    /// is.
    /// </summary>
    /// <remarks>
    /// The simplest settle in this file, because a collection holds no
    /// photographs. There is nothing to match against a library's contents and
    /// therefore nothing that can arrive before its pictures do: a shelf is
    /// never held, and a machine that has not finished scanning still gets every
    /// shelf in the house the first time it shares.
    ///
    /// <para>Unlike an album there is no proposed form to skip. Nobody can
    /// propose a theme, so every collection in a payload is something a person
    /// typed and every one of them is worth taking.</para>
    /// </remarks>
    private static List<SharedCollection> SettleCollections(
        DecisionSet mine, List<DecisionSet> accepted)
    {
        Dictionary<Guid, SharedCollection> ours =
            mine.Collections.ToDictionary(collection => collection.PublicId);

        Dictionary<Guid, SharedCollection> winners = new(ours);

        foreach (SharedCollection collection in accepted.SelectMany(them => them.Collections))
        {
            winners[collection.PublicId] =
                winners.TryGetValue(collection.PublicId, out SharedCollection? standing)
                    ? Settle(standing, collection)
                    : collection;
        }

        return
        [
            .. winners.Values.Where(winner =>
                !ours.TryGetValue(winner.PublicId, out SharedCollection? was) || was != winner),
        ];
    }

    private static SharedCollection Settle(SharedCollection mine, SharedCollection theirs)
    {
        int byDate = mine.NamedUtc.CompareTo(theirs.NamedUtc);

        SharedCollection named = byDate != 0
            ? (byDate > 0 ? mine : theirs)
            : string.CompareOrdinal(mine.Name, theirs.Name) >= 0 ? mine : theirs;

        return named with { DeletedUtc = Earliest(mine.DeletedUtc, theirs.DeletedUtc) };
    }

    /// <param name="settling">
    /// The albums this merge has just settled, so that a membership naming one
    /// this library has never held can tell a row about to be written from a
    /// row nobody will write.
    /// </param>
    private static (List<SharedAlbumMove> Moves, List<SharedAlbumMembership> Held) SettleMemberships(
        DecisionSet mine,
        List<DecisionSet> accepted,
        LibraryContents here,
        IReadOnlyList<SharedAlbum> settling)
    {
        Dictionary<AssetKey, SharedAlbumMembership> ours = mine.Memberships.ToDictionary(m => m.Photo);
        Dictionary<AssetKey, SharedAlbumMembership> winners = new(ours);
        List<SharedAlbumMembership> held = [];

        Albums albums = Albums.Of(mine, settling);

        // One machine at a time, because an album is named by the identity the
        // machine that published it uses, and working out what that names here
        // needs that machine's own album rows.
        foreach (DecisionSet them in accepted)
        {
            Dictionary<Guid, SharedAlbum> theirs = [];
            foreach (SharedAlbum album in them.Albums)
            {
                theirs[album.PublicId] = album;
            }

            foreach (SharedAlbumMembership membership in them.Memberships)
            {
                // Refused before it is parked rather than after. A scan cannot
                // make an answer takeable that this library has already refused,
                // so holding one buys a waiting answer that the very sweep which
                // reads it back will drop - and release as applied on the way
                // out, having written nothing.
                if (albums.Placed(membership.Album, theirs) is not Guid album)
                {
                    continue;
                }

                if (!here.Photographs.Contains(membership.Photo))
                {
                    // Parked under the name this library will look the album up
                    // by, not the one it arrived under. What comes back to
                    // settle it later arrives without the set that carried it,
                    // so this is the last moment the publishing machine's albums
                    // can be read - and an answer parked under a name only that
                    // machine uses would fail to place on the very sweep that
                    // exists to make it land.
                    held.Add(album == membership.Album
                        ? membership
                        : membership with { Album = album });

                    continue;
                }

                SharedAlbumMembership settled =
                    album == membership.Album ? membership : membership with { Album = album };

                winners[settled.Photo] =
                    winners.TryGetValue(settled.Photo, out SharedAlbumMembership? standing)
                    && Wins(standing.AddedUtc, standing.DecidedBy, settled.AddedUtc, settled.DecidedBy)
                        ? standing
                        : settled;
            }
        }

        return (
            [
                .. winners.Values
                    .Select(winner => new
                    {
                        Winner = winner,
                        Was = ours.TryGetValue(winner.Photo, out SharedAlbumMembership? was) ? was : null,
                    })
                    .Where(move => move.Was?.Album != move.Winner.Album)
                    .Select(move => new SharedAlbumMove(
                        move.Winner.Photo,
                        move.Was?.Album,
                        move.Winner.Album,
                        move.Winner.AddedUtc,
                        move.Winner.DecidedBy)),
            ],
            held);
    }

    /// <summary>
    /// This library's own albums, by both of the names another machine can call
    /// one.
    /// </summary>
    /// <remarks>
    /// An album somebody made mints its identity once and keeps it everywhere -
    /// the merge creates the row with the identity it arrived under - so those
    /// two machines agree on. A proposal does not: the pass deletes and
    /// reinserts it, so every machine's row for the same run of days carries a
    /// different <see cref="SharedAlbum.PublicId"/>, and the days are the only
    /// thing both of them can say. That is what <see cref="Identity"/> already
    /// settles for the albums themselves, and a membership has to be read
    /// through the same rule or the two passes disagree about what an album is.
    /// </remarks>
    private sealed class Albums
    {
        private readonly Dictionary<string, SharedAlbum> _byIdentity = [];
        private readonly Dictionary<Guid, SharedAlbum> _byPublicId = [];
        private readonly Dictionary<string, SharedAlbum> _arriving = [];

        /// <param name="settling">
        /// What the album pass just concluded, which is the other half of the
        /// question. An album this library has never held is about to be created
        /// - unless the very thing arriving about it is that somebody threw it
        /// away, and then it is not.
        /// </param>
        public static Albums Of(DecisionSet mine, IReadOnlyList<SharedAlbum> settling)
        {
            var albums = new Albums();

            foreach (SharedAlbum album in mine.Albums)
            {
                albums._byIdentity[Identity(album)] = album;
                albums._byPublicId[album.PublicId] = album;
            }

            foreach (SharedAlbum album in settling)
            {
                albums._arriving[Identity(album)] = album;
            }

            return albums;
        }

        /// <summary>
        /// What this library would call the album a membership names, or null
        /// where it will not take that membership at all.
        /// </summary>
        /// <remarks>
        /// Two refusals, and both are decisions rather than accidents. They were
        /// neither before: the album was looked up by the publishing machine's
        /// identity when the rows were written, that lookup quietly failed, and
        /// the membership was dropped with no row, no waiting answer and no
        /// count - which is somebody's evening of tidying disappearing without a
        /// word. Refusing here instead means the plan never carries a move that
        /// cannot land, so "merging twice changes nothing" stays true and the
        /// Sharing screen stops offering answers that do nothing when taken.
        ///
        /// <para><strong>Gone here.</strong> A tombstone is kept for ever and
        /// beats any date, so an album this library has deleted is not coming
        /// back and nothing may join it. The other machine learns of the
        /// deletion on its own next merge and stops publishing them.</para>
        ///
        /// <para><strong>Still only a suggestion here.</strong> A proposal's
        /// contents are derived, and the rebuild owns them - it prunes whatever
        /// its own clustering no longer claims. Taking another machine's
        /// memberships into one would put that machine's answers up against this
        /// one's next scan, which is the mirror of the rule that stops a
        /// proposal's contents being published in the first place. What is
        /// missing is that <em>keeping</em> a suggestion does not travel; until
        /// it does, this is the honest answer rather than a write that a scan
        /// may undo.</para>
        /// </remarks>
        public Guid? Placed(Guid named, IReadOnlyDictionary<Guid, SharedAlbum> theirs)
        {
            SharedAlbum? album = theirs.GetValueOrDefault(named);

            SharedAlbum? mine =
                album is not null
                && _byIdentity.TryGetValue(Identity(album), out SharedAlbum? matched)
                    ? matched
                    : _byPublicId.GetValueOrDefault(named);

            // One this library has never held, which this merge is about to
            // create - but only if what is arriving about it is that it exists.
            // An album that arrives already thrown away is never written, and
            // promising a membership a row that nobody will make is how the
            // same move came to be planned on every merge and applied on none.
            if (mine is null)
            {
                if (album is null || !_arriving.TryGetValue(Identity(album), out SharedAlbum? coming))
                {
                    return named;
                }

                // The identity it will be created under is the one the album
                // pass settled on, which is not always the one this membership
                // arrived naming: two machines can hold the same run of days and
                // only one of them wins.
                return Refused(coming) ? null : coming.PublicId;
            }

            return Refused(mine) ? null : mine.PublicId;
        }

        private static bool Refused(SharedAlbum album) =>
            album.DeletedUtc is not null || album.Origin == AlbumOrigin.Proposed;
    }

    private static (List<SharedAlbumRejection> Applied, List<SharedAlbumRejection> Held) SettleRejections(
        DecisionSet mine, List<DecisionSet> accepted, LibraryContents here)
    {
        HashSet<(AssetKey Photo, string Key)> ours =
            [.. mine.Rejections.Select(r => (r.Photo, r.ProposalKey))];

        List<SharedAlbumRejection> applied = [];
        List<SharedAlbumRejection> held = [];
        HashSet<(AssetKey, string)> seen = [];

        // Rejections only ever accumulate, so two machines never disagree about
        // one: the merge is a union rather than a contest.
        foreach (SharedAlbumRejection rejection in accepted.SelectMany(them => them.Rejections))
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
