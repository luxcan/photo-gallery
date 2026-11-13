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
        AlbumSettlement albums = SettleAlbums(mine, accepted, here);

        // A membership with nowhere to go here is refused, not parked: the
        // machine that sent it sends it again on every sync, and the day this
        // library can take it, it lands.
        Memberships memberships = SettleMemberships(mine, accepted, here, albums);
        (List<SharedAlbumRejection> rejections, List<SharedAlbumRejection> heldRejections) =
            SettleRejections(mine, accepted, here);

        return new MergePlan(
            people,
            faces.Answers,
            faces.Withdrawn,
            faces.Strangers,
            faces.Recognised,
            turns,
            albums.Planned,
            memberships.Moves,
            rejections,
            SeedEras(mine, accepted),
            SettleCollections(mine, accepted),
            new HeldAnswers(
                faces.HeldAnswers,
                faces.HeldStrangers,
                heldTurns,
                memberships.Held,
                heldRejections),
            OfferJoins(mine, accepted, people),
            refused,
            links,
            renames,
            pairings)
        {
            Dismissals = albums.Dismissals,
            DismissedHere = albums.DismissedHere,
        };
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
    ///
    /// <para><strong>Every waiting answer comes out as one of four
    /// things.</strong> Landing, where the plan carries a change for it. Still
    /// held, where its photograph or its face is not here yet. Unplaced, where
    /// the photograph is here and the album it names is not. And settled, where
    /// nothing needs writing because what it says is already true here, a later
    /// answer has beaten it, or the album or the person it names has been
    /// deleted. The sweep lets go of the first and the last and reports them
    /// apart, because only one of them is work arriving.</para>
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
        Memberships memberships = SettleMemberships(mine, parked, here, AlbumSettlement.None);
        (List<SharedAlbumRejection> rejections, List<SharedAlbumRejection> heldRejections) =
            SettleRejections(mine, parked, here);

        // No people, albums or eras. Those are not about a photograph, so they
        // were never held: they landed on the merge that carried them.
        MergePlan plan = MergePlan.Nothing with
        {
            Answers = faces.Answers,
            Withdrawn = faces.Withdrawn,
            Strangers = faces.Strangers,
            Recognised = faces.Recognised,
            Turns = turns,
            Moves = memberships.Moves,
            Rejections = rejections,

            // A move with nowhere to go is kept, where a merge would refuse it.
            // A merge can afford to: the machine that sent it sends it again on
            // every sync. Nothing sends a held answer again - for this library's
            // own decisions, parked when a scan lost their photograph, the row is
            // the only copy there is.
            Held = new HeldAnswers(
                faces.HeldAnswers,
                faces.HeldStrangers,
                heldTurns,
                [
                    .. memberships.Held,
                    .. memberships.Nowhere.Where(move => !here.Photographs.Contains(move.Photo)),
                ],
                heldRejections),
            Unplaced =
            [
                .. memberships.Nowhere.Where(move => here.Photographs.Contains(move.Photo)),
            ],
        };

        return plan with { Settled = Settled(waiting, plan, here) };
    }

    /// <summary>
    /// The waiting answers a sweep is done with although its plan writes nothing
    /// for them.
    /// </summary>
    /// <remarks>
    /// Read off the plan rather than decided beside it, so there is one set of
    /// rules and not two: whatever the ordinary settling concluded, an answer
    /// that is neither still waiting nor carried by one of the plan's changes was
    /// settled by something already here.
    ///
    /// <para>Each change answers for one waiting answer at most. Two waiting
    /// moves for one photograph come to one move, and counting both as applied
    /// was the same overclaim as counting the one that lost.</para>
    /// </remarks>
    private static HeldAnswers Settled(HeldAnswers waiting, MergePlan plan, LibraryContents here)
    {
        HashSet<FaceAnswer> named = [.. plan.Answers];
        HashSet<StrangerFace> marked = [.. plan.Strangers];
        HashSet<PhotoTurn> turned = [.. plan.Turns];
        HashSet<(AssetKey, DateTime, Guid)> moved =
            [.. plan.Moves.Select(move => (move.Photo, move.AddedUtc, move.DecidedBy))];
        HashSet<SharedAlbumRejection> refused = [.. plan.Rejections];

        // A face answer is planned under the box this library found, not the one
        // it arrived with, so it is looked for where it landed.
        return new HeldAnswers(
            Unwritten(
                waiting.Answers,
                plan.Held.Answers,
                answer => Landed(here, answer.Face) is FaceKey face
                       && named.Remove(answer with { Face = face })),
            Unwritten(
                waiting.Strangers,
                plan.Held.Strangers,
                stranger => Landed(here, stranger.Face) is FaceKey face
                         && marked.Remove(stranger with { Face = face })),
            Unwritten(waiting.Turns, plan.Held.Turns, turned.Remove),
            Unwritten(
                waiting.Memberships,
                [.. plan.Held.Memberships, .. plan.Unplaced],
                membership => moved.Remove(
                    (membership.Photo, membership.AddedUtc, membership.DecidedBy))),
            Unwritten(waiting.Rejections, plan.Held.Rejections, refused.Remove));
    }

    /// <summary>The waiting answers neither still waiting nor written by the plan.</summary>
    /// <param name="written">
    /// Takes the plan's change for an answer where it carries one, so that a
    /// change spent on one answer cannot be claimed by the next.
    /// </param>
    private static List<T> Unwritten<T>(
        IReadOnlyList<T> waiting, IEnumerable<T> stillWaiting, Func<T, bool> written)
    {
        HashSet<T> kept = [.. stillWaiting];
        List<T> settled = [];

        foreach (T answer in waiting)
        {
            if (!kept.Contains(answer) && !written(answer))
            {
                settled.Add(answer);
            }
        }

        return settled;
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

        // Everybody deleted anywhere this merge can see. A tombstone always wins
        // whatever its date, so a person deleted here stays deleted - and an
        // answer about them has to lose with them, or it is written onto the
        // tombstone and, being the later name, unseats the one a face already
        // had. Asked before the face is, for the reason a deleted album is: no
        // scan can make the answer takeable, so there is nothing to wait for.
        HashSet<Guid> gone =
        [
            .. mine.People
                .Concat(accepted.SelectMany(them => them.People))
                .Where(person => person.DeletedUtc is not null)
                .Select(person => person.PublicId),
        ];

        foreach (FaceAnswer answer in accepted.SelectMany(them => them.Answers))
        {
            if (gone.Contains(answer.Person))
            {
                continue;
            }

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

    /// <summary>
    /// Whether a merge writes a row for an album this library has none for.
    /// </summary>
    /// <remarks>
    /// A tombstone always. With no row this library cannot tell "deleted" from
    /// "never heard of", and a deleted kept suggestion's run of days is what
    /// stops its next scan suggesting the days somebody threw away. An album
    /// somebody kept or made, too. A live suggestion is derived, though, so its
    /// row is this machine's own to build: what travels about one is a name,
    /// and a name with nothing to sit on waits for the rebuild that makes it.
    ///
    /// <para>Public because the write asks exactly this, and the two must not
    /// disagree. A plan carrying an album the write then skips is a plan that
    /// carries it again on the next merge, and the one after.</para>
    /// </remarks>
    public static bool CreatesARow(SharedAlbum album)
    {
        ArgumentNullException.ThrowIfNull(album);

        return album.DeletedUtc is not null || album.Origin != AlbumOrigin.Proposed;
    }

    /// <summary>
    /// Settles every album, and the dismissals that compete with keeping one.
    /// </summary>
    /// <remarks>
    /// <strong>Every account of an album at once, not two at a time.</strong> A
    /// run of days can be kept on one laptop, dismissed on a second and kept
    /// again on a third, and which of those came last is a question about all
    /// three. Folded two at a time, the answer could depend on the order the
    /// files were read in, and a date could ride from one laptop's album onto
    /// another's. So each album's accounts are gathered first - this library's
    /// own first - and then settled in one of three ways.
    ///
    /// <para><strong>Deleted</strong>, where any account is a tombstone. That
    /// beats every keep and every dismissal whatever the dates, because deleting
    /// a kept album means never suggest these days again. Settled exactly as it
    /// always was.</para>
    ///
    /// <para><strong>Lost</strong>, where somebody kept the suggestion and a
    /// dismissal of the same days came later. A keep that cannot say when it was
    /// made loses to any dismissal that can, and a keep and a dismissal in the
    /// same instant leave it kept. Nothing about a keep that lost travels - not
    /// its name, and not its shelf, which would keep the suggestion all over
    /// again - and where the keep is this library's own, it is taken away.</para>
    ///
    /// <para><strong>Live</strong>, everything else: name, keep, shelf and cover
    /// each settled on their own.</para>
    /// </remarks>
    /// <param name="here">
    /// What this machine has actually indexed, which is what decides whether a
    /// cover can be taken. A cover names a photograph, so it is the one thing
    /// about an album that can arrive before its picture does.
    /// </param>
    private static AlbumSettlement SettleAlbums(
        DecisionSet mine, List<DecisionSet> accepted, LibraryContents here)
    {
        Dictionary<string, SharedAlbum> ours = mine.Albums.ToDictionary(Identity);
        Dictionary<string, SharedAlbumDismissal> dismissed = SettleDismissals(mine, accepted);

        Dictionary<string, List<SharedAlbum>> accounts = [];

        foreach (SharedAlbum album in mine.Albums.Concat(Theirs(accepted)))
        {
            string identity = Identity(album);

            if (!accounts.TryGetValue(identity, out List<SharedAlbum>? told))
            {
                accounts[identity] = told = [];
            }

            told.Add(album);
        }

        Dictionary<string, SharedAlbum> standing = [];
        HashSet<string> lost = [];
        List<SharedAlbumDismissal> dismissedHere = [];

        foreach ((string identity, List<SharedAlbum> told) in accounts)
        {
            if (told.Any(album => album.DeletedUtc is not null))
            {
                standing[identity] = told.Aggregate(Settle);
                continue;
            }

            if (LostTo(told, dismissed) is SharedAlbumDismissal dismissal)
            {
                lost.Add(identity);

                if (ours.TryGetValue(identity, out SharedAlbum? kept)
                    && kept.Origin == AlbumOrigin.Accepted)
                {
                    dismissedHere.Add(dismissal);
                }

                continue;
            }

            standing[identity] = told.Aggregate(SettleLive);
        }

        return new AlbumSettlement(
            [
                .. standing.Values
                    .Select(winner => Keepable(winner, ours, here))
                    .Where(winner => ours.TryGetValue(Identity(winner), out SharedAlbum? was)
                        ? was != winner
                        : CreatesARow(winner)),
            ],
            standing,
            lost,
            [.. dismissed.Values.Where(winner => !mine.Dismissals.Contains(winner))],
            dismissedHere);
    }

    /// <summary>
    /// The other machines' albums worth settling, in an order that does not
    /// depend on the order their files were read in.
    /// </summary>
    /// <remarks>
    /// A proposal nobody has renamed or thrown away carries no decision at all.
    /// The other machine makes its own from the same photographs, and better
    /// ones once it has the confirmations that came with this.
    ///
    /// <para>In order of identity, because two accounts can tie on everything a
    /// settle compares - two laptops that both kept the same days under the
    /// same name - and then the first of them stands. The folder lists files in
    /// whatever order the disk keeps them, so without this two laptops could
    /// settle one album under two identities.</para>
    /// </remarks>
    private static IEnumerable<SharedAlbum> Theirs(List<DecisionSet> accepted) =>
        accepted
            .SelectMany(them => them.Albums)
            .Where(album => album.Origin != AlbumOrigin.Proposed
                         || album.NamedUtc is not null
                         || album.DeletedUtc is not null)
            .OrderBy(album => album.PublicId);

    /// <summary>
    /// The latest dismissal of each run of days anybody in the house has made.
    /// </summary>
    /// <remarks>
    /// Only which dismissal of those days is the latest - by its moment, then
    /// by the machine, so every laptop picks the same one. What it beats is
    /// decided with the albums. Never held: a dismissal is about days rather
    /// than a photograph, so there is nothing for it to wait for. One naming no
    /// days is passed over, because a file is read from a drive another
    /// machine may be writing to and is trusted no further than that.
    /// </remarks>
    private static Dictionary<string, SharedAlbumDismissal> SettleDismissals(
        DecisionSet mine, List<DecisionSet> accepted)
    {
        Dictionary<string, SharedAlbumDismissal> latest = [];

        foreach (SharedAlbumDismissal dismissal in
                 mine.Dismissals.Concat(accepted.SelectMany(them => them.Dismissals)))
        {
            if (string.IsNullOrWhiteSpace(dismissal.ProposalKey))
            {
                continue;
            }

            latest[dismissal.ProposalKey] =
                latest.TryGetValue(dismissal.ProposalKey, out SharedAlbumDismissal? standing)
                && Wins(standing.DismissedUtc, standing.DecidedBy, dismissal.DismissedUtc, dismissal.DecidedBy)
                    ? standing
                    : dismissal;
        }

        return latest;
    }

    /// <summary>
    /// The dismissal every keep of these days lost to, or null where they did
    /// not lose.
    /// </summary>
    /// <remarks>
    /// Judged against the latest keep anybody can date. A keep that cannot say
    /// when it was made ranks below every one that can, and loses to any
    /// dismissal at all - it is a moment nobody knows, and a moment nobody
    /// knows must not beat a real one. On the same instant the keep stands: an
    /// album kept by mistake is one press from gone, and one dismissed by
    /// mistake is already gone.
    /// </remarks>
    private static SharedAlbumDismissal? LostTo(
        List<SharedAlbum> accounts, Dictionary<string, SharedAlbumDismissal> dismissed)
    {
        List<SharedAlbum> keeps = [.. accounts.Where(album => album.Origin == AlbumOrigin.Accepted)];

        if (keeps.Count == 0
            || accounts[0].ProposalKey is not string days
            || !dismissed.TryGetValue(days, out SharedAlbumDismissal? dismissal))
        {
            return null;
        }

        DateTime? keptAt = keeps.Max(keep => keep.KeptUtc);

        return keptAt is null || keptAt < dismissal.DismissedUtc ? dismissal : null;
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

    /// <summary>
    /// Which of two accounts of an album stands, where somebody has thrown it
    /// away.
    /// </summary>
    /// <remarks>
    /// The earliest deletion stands, and everything else about the album rides
    /// on whichever account won the name. Exactly what it always was: a
    /// tombstone is final, so nothing about keeping or dismissing can change
    /// what it settles to.
    /// </remarks>
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
    /// Which of two accounts of an album stands, where nobody has thrown it
    /// away: each answer about it settled on its own.
    /// </summary>
    /// <remarks>
    /// <strong>Keeping is its own answer.</strong> It used to ride on whichever
    /// account won the name, so a suggestion kept on one laptop and merely
    /// suggested on another stayed a suggestion whenever the names tied - which,
    /// for two laptops that grouped the same days, is nearly always. The
    /// stronger answer stands instead: a suggestion is the app's guess and a
    /// keep is a person's answer, and an album somebody made is stronger than
    /// either. When it was kept is the latest moment anybody can give, so a keep
    /// arriving from a release that never said when cannot wipe out one that
    /// did.
    ///
    /// <para><strong>The name</strong> is the later one typed. Where nobody
    /// typed one, the name it has where it was kept stands over a suggestion's:
    /// two names the app chose are equally good guesses, and a laptop taking in
    /// a kept album must not rename it, on the laptop that kept it, to a guess
    /// nobody there has seen. Then the text, so two laptops settle the same
    /// tie the same way, and a tie on everything keeps this library's own -
    /// which is what lets the next merge find nothing to do.</para>
    ///
    /// <para><strong>The cover</strong> on its own date as ever. Two chosen in
    /// the same instant go the way the name does, the kept album's first, and
    /// only then to the photograph's own key.</para>
    /// </remarks>
    private static SharedAlbum SettleLive(SharedAlbum mine, SharedAlbum theirs)
    {
        int byDate = Nullable.Compare(mine.NamedUtc, theirs.NamedUtc);

        SharedAlbum named = byDate != 0
            ? (byDate > 0 ? mine : theirs)
            : Keeping(mine, theirs)
              ?? (string.CompareOrdinal(mine.Name, theirs.Name) >= 0 ? mine : theirs);

        SharedAlbum shelved = Shelf(mine, theirs);

        SharedAlbum covered =
            Nullable.Compare(mine.CoverChosenUtc, theirs.CoverChosenUtc) == 0
                ? Keeping(mine, theirs) ?? Cover(mine, theirs)
                : Cover(mine, theirs);

        return named with
        {
            // Proposed, then Accepted, then Made: the order the values are
            // declared in, which is the order of how much a person said.
            Origin = mine.Origin > theirs.Origin ? mine.Origin : theirs.Origin,
            KeptUtc = Latest(mine.KeptUtc, theirs.KeptUtc),
            Shelf = shelved.Shelf,
            ShelvedUtc = shelved.ShelvedUtc,
            Cover = covered.Cover,
            CoverChosenUtc = covered.CoverChosenUtc,
        };
    }

    /// <summary>
    /// The one of two accounts somebody kept, where only one of them is kept.
    /// </summary>
    private static SharedAlbum? Keeping(SharedAlbum mine, SharedAlbum theirs) =>
        (mine.Origin != AlbumOrigin.Proposed, theirs.Origin != AlbumOrigin.Proposed) switch
        {
            (true, false) => mine,
            (false, true) => theirs,
            _ => null,
        };

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

    /// <param name="settled">
    /// What the album pass just concluded, so that a membership is placed
    /// against what each album is about to become rather than what it was. A
    /// suggestion being taken in as kept takes its photographs on this merge,
    /// an album being taken away takes none, and one this library has never
    /// held is written only if what arrives about it is that it exists.
    /// </param>
    private static Memberships SettleMemberships(
        DecisionSet mine,
        List<DecisionSet> accepted,
        LibraryContents here,
        AlbumSettlement settled)
    {
        Albums albums = Albums.Of(mine, settled);

        // A photograph in an album this merge takes away is no longer standing
        // anywhere: the album goes, and the photograph's place in it with it.
        // Counted as standing, its membership would beat an earlier filing
        // somewhere else and then vanish with the album, leaving the
        // photograph in nothing until the next merge put it right.
        Dictionary<AssetKey, SharedAlbumMembership> ours = mine.Memberships
            .Where(membership => !albums.IsGoing(membership.Album))
            .ToDictionary(m => m.Photo);

        Dictionary<AssetKey, SharedAlbumMembership> winners = new(ours);
        List<SharedAlbumMembership> held = [];
        List<SharedAlbumMembership> nowhere = [];
        Refusals refusals = Refusals.Of(mine, accepted);

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
                // Beaten before anything is asked about its album. Where a
                // photograph belongs is settled on when that was decided and by
                // whom, and two machines - or this library and an answer it has
                // been holding - can call one album by two names. Asked first,
                // an answer this library already holds is known for what it is
                // whatever album it names, which is how a held move that could
                // not be placed is let go once a sync has placed it.
                if (winners.TryGetValue(membership.Photo, out SharedAlbumMembership? standing)
                    && Wins(standing.AddedUtc, standing.DecidedBy, membership.AddedUtc, membership.DecidedBy))
                {
                    continue;
                }

                Placing placing = albums.Placed(membership.Album, theirs);

                // Thrown away, and a tombstone is final. No scan can make the
                // album come back, so the answer is neither parked nor kept:
                // the photograph stays where it is, which is already true.
                if (placing.Gone)
                {
                    continue;
                }

                // Nowhere to put it yet, and asked before the photograph is, so
                // that a merge can refuse it before it is ever parked. What it
                // means is the caller's to say - see Merge and Rejoin.
                if (placing.Album is not Guid album)
                {
                    nowhere.Add(membership);
                    continue;
                }

                SharedAlbumMembership placed =
                    album == membership.Album ? membership : membership with { Album = album };

                // Taken out of that album since it was put there, here or on
                // another computer. Beaten like an earlier filing, so a
                // photograph somebody took out is not put straight back by the
                // computer that has not heard yet.
                if (refusals.Since(placed, albums.Here(album) ?? theirs.GetValueOrDefault(membership.Album))
                    is not null)
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
                    held.Add(placed);
                    continue;
                }

                winners[placed.Photo] = placed;
            }
        }

        // This library's own filings that somebody has since taken out - the
        // half of a removal that travels. Only where nothing newer has moved
        // the photograph elsewhere in this merge.
        List<SharedAlbumMove> takenOut = [];
        foreach (SharedAlbumMembership standing in ours.Values)
        {
            if (winners.GetValueOrDefault(standing.Photo) == standing
                && refusals.Since(standing, albums.Here(standing.Album))
                    is SharedAlbumRejection refused)
            {
                winners.Remove(standing.Photo);
                takenOut.Add(new SharedAlbumMove(
                    standing.Photo, standing.Album, To: null, refused.RejectedUtc, refused.DecidedBy));
            }
        }

        return new Memberships(
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
                .. takenOut,
            ],
            held,
            nowhere);
    }

    /// <summary>
    /// The latest refusal of each photograph from each album, across this
    /// library and every computer it has heard from.
    /// </summary>
    /// <remarks>
    /// Taking a photograph out of an album is remembered as a refusal, and
    /// refusals travel; a membership did not know about them, so the computer
    /// that had not heard yet put the photograph straight back on the next
    /// sync. A refusal later than a membership now beats it. Later only, and
    /// that is what makes putting a photograph back work: the refusal is never
    /// cleared, but a membership made after it is newer and stands.
    /// </remarks>
    private sealed class Refusals
    {
        private readonly Dictionary<(AssetKey Photo, string Key), SharedAlbumRejection> _latest = [];

        public static Refusals Of(DecisionSet mine, IEnumerable<DecisionSet> others)
        {
            var refusals = new Refusals();

            foreach (SharedAlbumRejection rejection in
                     mine.Rejections.Concat(others.SelectMany(them => them.Rejections)))
            {
                if (AlbumRefusalKey.IsLocalOnly(rejection.ProposalKey))
                {
                    continue;
                }

                (AssetKey, string) key = (rejection.Photo, rejection.ProposalKey);

                if (!refusals._latest.TryGetValue(key, out SharedAlbumRejection? standing)
                    || rejection.RejectedUtc > standing.RejectedUtc)
                {
                    refusals._latest[key] = rejection;
                }
            }

            return refusals;
        }

        /// <summary>
        /// The refusal that takes this photograph out of this album since it was
        /// put there, or null.
        /// </summary>
        /// <param name="album">The album as this library knows it, or null when unknown.</param>
        public SharedAlbumRejection? Since(SharedAlbumMembership membership, SharedAlbum? album)
        {
            if (album is null)
            {
                return null;
            }

            string key = album.ProposalKey ?? AlbumRefusalKey.Made(album.PublicId);

            return _latest.TryGetValue((membership.Photo, key), out SharedAlbumRejection? refused)
                   && refused.RejectedUtc > membership.AddedUtc
                ? refused
                : null;
        }
    }

    /// <summary>What a merge concluded about which album each photograph is in.</summary>
    /// <param name="Held">The memberships to park until their photograph is indexed.</param>
    /// <param name="Nowhere">
    /// The ones with nowhere to go here yet, which a merge refuses and a sweep
    /// keeps. A membership beaten by a later one, or naming an album this library
    /// has thrown away, is in none of the three lists.
    /// </param>
    private sealed record Memberships(
        List<SharedAlbumMove> Moves,
        List<SharedAlbumMembership> Held,
        List<SharedAlbumMembership> Nowhere);

    /// <summary>
    /// Where a membership goes in this library, or why it goes nowhere.
    /// </summary>
    /// <param name="Album">The album it goes into here, where there is one.</param>
    /// <param name="Gone">
    /// Whether there is none because the album has been thrown away, which is
    /// final - unlike having no row for it yet, which a later sync can change.
    /// </param>
    private readonly record struct Placing(Guid? Album, bool Gone)
    {
        public static Placing Thrown => new(null, Gone: true);

        public static Placing Nowhere => new(null, Gone: false);
    }

    /// <summary>What the album pass concluded.</summary>
    /// <param name="Planned">The albums this library should write.</param>
    /// <param name="Standing">
    /// What every album here or arriving settled to, written or not - which is
    /// what a membership has to be placed against. A run of days whose keeps
    /// lost has no entry: nothing about it stands.
    /// </param>
    /// <param name="Lost">The runs of days whose keeps a later dismissal beat.</param>
    /// <param name="Dismissals">The latest dismissals this library has not stored yet.</param>
    /// <param name="DismissedHere">This library's own keeps those dismissals take away.</param>
    private sealed record AlbumSettlement(
        List<SharedAlbum> Planned,
        IReadOnlyDictionary<string, SharedAlbum> Standing,
        IReadOnlySet<string> Lost,
        List<SharedAlbumDismissal> Dismissals,
        List<SharedAlbumDismissal> DismissedHere)
    {
        /// <summary>
        /// Nothing settled at all: every album judged as it stands here, which
        /// is what a sweep of waiting answers places them against. No album
        /// arrives with a waiting answer.
        /// </summary>
        public static AlbumSettlement None { get; } =
            new([], new Dictionary<string, SharedAlbum>(), new HashSet<string>(), [], []);
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
        private readonly AlbumSettlement _settled;

        private Albums(AlbumSettlement settled) => _settled = settled;

        /// <param name="settled">
        /// What the album pass just concluded, which is the other half of the
        /// question. Every album is placed against what it is about to become:
        /// one this library has never held is about to be created - unless the
        /// very thing arriving about it is that somebody threw it away - and one
        /// it holds may be about to be kept, thrown away, or taken away by a
        /// later dismissal.
        /// </param>
        public static Albums Of(DecisionSet mine, AlbumSettlement settled)
        {
            var albums = new Albums(settled);

            foreach (SharedAlbum album in mine.Albums)
            {
                albums._byIdentity[Identity(album)] = album;
                albums._byPublicId[album.PublicId] = album;
            }

            return albums;
        }

        /// <summary>
        /// Whether this library's album of that identity is on its way out in
        /// this merge: kept and beaten by a later dismissal, or about to become
        /// a tombstone.
        /// </summary>
        public bool IsGoing(Guid publicId) =>
            _byPublicId.TryGetValue(publicId, out SharedAlbum? album)
            && (_settled.Lost.Contains(Identity(album))
                || _settled.Standing.GetValueOrDefault(Identity(album))?.DeletedUtc is not null);

        /// <summary>This library's album of that identity, if it has one.</summary>
        public SharedAlbum? Here(Guid publicId) => _byPublicId.GetValueOrDefault(publicId);

        /// <summary>
        /// What this library would call the album a membership names, or why it
        /// will not take that membership.
        /// </summary>
        /// <remarks>
        /// Three refusals, and all are decisions rather than accidents. They were
        /// not always: the album was looked up by the publishing machine's
        /// identity when the rows were written, that lookup quietly failed, and
        /// the membership was dropped with no row, no waiting answer and no
        /// count - which is somebody's evening of tidying disappearing without a
        /// word. Refusing here instead means the plan never carries a move that
        /// cannot land, so "merging twice changes nothing" stays true and the
        /// Sharing screen stops offering answers that do nothing when taken.
        ///
        /// <para><strong>Gone here.</strong> A tombstone is kept for ever and
        /// beats any date, so an album this library has deleted is not coming
        /// back and nothing may join it - nor one this very merge is turning
        /// into a tombstone, which the write would skip. The other machine
        /// learns of the deletion on its own next merge and stops publishing
        /// them.</para>
        ///
        /// <para><strong>Still only a suggestion here.</strong> A proposal's
        /// contents are derived, and the rebuild owns them - it prunes whatever
        /// its own clustering no longer claims. Taking another machine's
        /// memberships into one would put that machine's answers up against this
        /// one's next scan, which is the mirror of the rule that stops a
        /// proposal's contents being published in the first place. Keeping now
        /// travels, and a suggestion somebody kept elsewhere is kept here by the
        /// same merge - so this guards only a suggestion nobody has kept, and a
        /// keep that a later dismissal beat. Both are honest refusals rather
        /// than a write a scan, or the next merge, would undo.</para>
        ///
        /// <para>A suggestion being kept by this merge takes the photographs
        /// under this library's own identity for it, because the write keeps
        /// the row where it stands rather than renumbering it.</para>
        ///
        /// <para><strong>Never heard of.</strong> No row by that name, and
        /// nothing arriving to make one. On a merge that cannot happen with an
        /// honest publisher, which sends every album its memberships name. On a
        /// sweep it is the ordinary case for a move parked by the first release
        /// under another machine's name: nothing arrives with a held answer but
        /// the answer, and a move planned into an album nobody here has was
        /// skipped by the write without a word, then forgotten as though it had
        /// landed.</para>
        /// </remarks>
        public Placing Placed(Guid named, IReadOnlyDictionary<Guid, SharedAlbum> theirs)
        {
            SharedAlbum? album = theirs.GetValueOrDefault(named);

            SharedAlbum? mine =
                album is not null
                && _byIdentity.TryGetValue(Identity(album), out SharedAlbum? matched)
                    ? matched
                    : _byPublicId.GetValueOrDefault(named);

            string? identity = mine is not null ? Identity(mine)
                : album is not null ? Identity(album)
                : null;

            // Kept somewhere, and a later dismissal beat every keep. Nothing
            // joins it here, whether this library holds it or not.
            if (identity is null || _settled.Lost.Contains(identity))
            {
                return Placing.Nowhere;
            }

            if (mine is not null)
            {
                Placing placing = Into(_settled.Standing.GetValueOrDefault(identity) ?? mine);

                return placing.Album is null ? placing : new Placing(mine.PublicId, Gone: false);
            }

            // One this library has never held, which this merge is about to
            // create - but only if what is arriving about it is that it exists.
            // An album that arrives already thrown away is never written, and
            // promising a membership a row that nobody will make is how the
            // same move came to be planned on every merge and applied on none.
            //
            // The identity it will be created under is the one the album pass
            // settled on, which is not always the one this membership arrived
            // naming: two machines can hold the same run of days and only one
            // of them wins.
            return _settled.Standing.TryGetValue(identity, out SharedAlbum? coming)
                ? Into(coming)
                : Placing.Nowhere;
        }

        private static Placing Into(SharedAlbum album) =>
            album.DeletedUtc is not null ? Placing.Thrown
            : album.Origin == AlbumOrigin.Proposed ? Placing.Nowhere
            : new Placing(album.PublicId, Gone: false);
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

            // A laptop on an older build names a made album by its own row
            // number, which here is a different album or none: taken, it kept a
            // photograph out of an unrelated album's "Find photos that fit".
            if (AlbumRefusalKey.IsLocalOnly(rejection.ProposalKey)
                || ours.Contains(key) || !seen.Add(key))
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

    /// <summary>The later of two moments, where a missing one loses to any.</summary>
    private static DateTime? Latest(DateTime? left, DateTime? right) =>
        left is null ? right
        : right is null ? left
        : left > right ? left : right;

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
