using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Two machines disagreeing, and how it is settled.
/// </summary>
/// <remarks>
/// The merge is a pure function of decision sets, which is exactly why it was
/// written that way: every rule in this feature is about two libraries holding
/// different answers, and expressed like this the interesting half needs no
/// working folder, no database and no shared folder to assert.
///
/// <para>The plan carries only what differs, so most of these read as "what
/// changes, and what conspicuously does not".</para>
/// </remarks>
public sealed class DecisionMergeTests
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tuesday = Monday.AddDays(1);
    private static readonly DateTime Wednesday = Monday.AddDays(2);
    private static readonly DateTime Now = Monday.AddDays(30);

    private static readonly Guid Ana = new("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid Ben = new("b0000000-0000-4000-8000-000000000002");

    [Fact]
    public void ANameGivenOnOneMachineArrivesOnTheOther()
    {
        // The whole feature in one assertion: no scan in between, and both
        // machines had already indexed the photograph.
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").Knows(Person(Ana, "Ana")).Confirms(face, Ana, Monday),
            here: Pictures.Holding(face));

        Assert.Equal("Ana", Assert.Single(plan.People).DisplayName);
        Assert.Equal(Ana, Assert.Single(plan.Answers).Person);
        Assert.Empty(plan.Held.Answers);
    }

    [Fact]
    public void MergingTwiceChangesNothingTheSecondTime()
    {
        // Idempotence as something you can look at rather than trust: carry out
        // the plan the merge actually produced, then merge the same answers
        // again. Asserted against a hand-written "and afterwards it looks like
        // this" it would only be testing what the person writing it believed -
        // which is how an earlier version of this passed while quietly proving
        // nothing.
        FaceKey face = Pictures.Face(@"2019\a.jpg");
        FaceKey background = Pictures.Face(@"2019\a.jpg", x: 300);
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        Guid genting = Guid.NewGuid();

        DecisionSet mum = new Machine("Mum")
            .Knows(Person(Ana, "Ana"))
            .Confirms(face, Ana, Monday)
            .CallsNobody(background, Monday)
            .Turns(photo, 90, Monday)
            .HasAlbum(Pictures.Album(genting, "Genting", Monday))
            .Puts(photo, genting, Monday)
            .Refuses(photo, "2019-03-03..2019-03-05", Monday)
            .Remembers(Ana, Monday, Wednesday, degrees: 0)
            .Set();

        LibraryContents here = Pictures.Holding(face, background);
        DecisionSet dad = new Machine("Dad").Set();

        MergePlan first = DecisionMerge.Merge(dad, [mum], here, Now);

        // Every kind of decision, so that the claim cannot quietly shrink to
        // "merging one name twice changes nothing".
        Assert.NotEmpty(first.People);
        Assert.NotEmpty(first.Answers);
        Assert.NotEmpty(first.Strangers);
        Assert.NotEmpty(first.Turns);
        Assert.NotEmpty(first.Albums);
        Assert.NotEmpty(first.Moves);
        Assert.NotEmpty(first.Rejections);
        Assert.NotEmpty(first.Eras);

        MergePlan second = DecisionMerge.Merge(dad.Apply(first), [mum], here, Now);

        Assert.True(second.ChangesNothing);
    }

    [Fact]
    public void AMergeStoppedHalfwayFinishesOnTheNextRun()
    {
        // Resumable for the same reason it is idempotent: the whole state is read
        // every time, so what was applied is applied and what was not is picked
        // up. Half a plan carried out is simply a library the next merge has less
        // to do to.
        FaceKey one = Pictures.Face(@"2019\a.jpg");
        FaceKey two = Pictures.Face(@"2019\b.jpg");

        DecisionSet mum = new Machine("Mum")
            .Knows(Person(Ana, "Ana"))
            .Confirms(one, Ana, Monday)
            .Confirms(two, Ana, Monday)
            .Set();

        LibraryContents here = Pictures.Holding(one, two);
        DecisionSet dad = new Machine("Dad").Set();

        MergePlan full = DecisionMerge.Merge(dad, [mum], here, Now);
        MergePlan half = full with { Answers = [full.Answers[0]] };
        DecisionSet stopped = dad.Apply(half);

        MergePlan rest = DecisionMerge.Merge(stopped, [mum], here, Now);

        Assert.Equal(full.Answers[1], Assert.Single(rest.Answers));
        Assert.True(DecisionMerge.Merge(stopped.Apply(rest), [mum], here, Now).ChangesNothing);
    }

    [Fact]
    public void TwoAnswersAboutOneFaceSettleOnTheLaterOne()
    {
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad").Confirms(face, Ana, Monday),
            theirs: new Machine("Mum").Confirms(face, Ben, Wednesday),
            here: Pictures.Holding(face));

        FaceAnswer landed = Assert.Single(plan.Answers);
        Assert.Equal(Ben, landed.Person);
        Assert.Equal(Wednesday, landed.DecidedUtc);

        // A face is one person, so the name that lost does not merely sit
        // underneath the one that won - the row goes, or the photograph ends up
        // holding two people in one box.
        Assert.Equal(Ana, Assert.Single(plan.Withdrawn).Person);
    }

    [Fact]
    public void AConfirmationBeatsAProposalEvenWhenTheProposalIsNewer()
    {
        // Clocks are the weak part of last-write-wins. They are close enough for
        // two human answers minutes apart and not something to bet a confirmed
        // name on against a machine-generated guess written afterwards.
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad").Proposes(face, Ben, Wednesday),
            theirs: new Machine("Mum").Confirms(face, Ana, Monday),
            here: Pictures.Holding(face));

        Assert.Contains(plan.Answers, a => a.Person == Ana && a.Source == AssignmentSource.Confirmed);
        Assert.DoesNotContain(plan.Answers, a => a.Source == AssignmentSource.Proposed);
    }

    [Fact]
    public void ClearingANameDoesNotStopThatPersonBeingProposedThereAgain()
    {
        // The reason unnaming has a source of its own. Routed through Rejected it
        // would suppress that person for that face for good, on every machine -
        // so somebody who cleared a name because they picked the wrong one could
        // never be offered the right answer again.
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad").Confirms(face, Ana, Monday),
            theirs: new Machine("Mum").Says(face, Ana, AssignmentSource.Cleared, Wednesday),
            here: Pictures.Holding(face));

        Assert.Equal(AssignmentSource.Cleared, Assert.Single(plan.Answers).Source);
    }

    [Fact]
    public void AConfirmationAndNobodyAreBothHumanAnswersAndTheLaterOneWins()
    {
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan stranger = Merge(
            mine: new Machine("Dad").Confirms(face, Ana, Monday),
            theirs: new Machine("Mum").CallsNobody(face, Wednesday),
            here: Pictures.Holding(face));

        Assert.Single(stranger.Strangers);
        Assert.Empty(stranger.Answers);
        Assert.Equal(Ana, Assert.Single(stranger.Withdrawn).Person);

        MergePlan named = Merge(
            mine: new Machine("Dad").CallsNobody(face, Monday),
            theirs: new Machine("Mum").Confirms(face, Ana, Wednesday),
            here: Pictures.Holding(face));

        // The mark has to come off, or the name lands on a face nothing shows.
        Assert.Equal(face, Assert.Single(named.Recognised));
        Assert.Equal(Ana, Assert.Single(named.Answers).Person);
    }

    [Fact]
    public void AnswersAboutPhotographsThisLibraryHasNotIndexedAreHeld()
    {
        // The single most important merge rule in the feature. Without it the
        // order of operations becomes something the user has to get right, and
        // getting it wrong loses an evening's work silently.
        FaceKey here = Pictures.Face(@"2019\a.jpg");
        FaceKey elsewhere = Pictures.Face(@"2026 Phone Dump\b.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum")
                .Confirms(here, Ana, Monday)
                .Confirms(elsewhere, Ana, Monday),
            here: Pictures.Holding(here));

        Assert.Single(plan.Answers);
        Assert.Equal(1, plan.Held.Count);
        Assert.Equal(elsewhere, Assert.Single(plan.Held.Answers).Face);
    }

    [Fact]
    public void AnAnswerAboutAPhotographWhoseFacesHaveNotBeenFoundYetWaitsToo()
    {
        // Indexed is not enough: a photograph just crawled has no faces, so a
        // sweep run any earlier than the face phase would find nothing to apply
        // the answers to and put them all back.
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").Confirms(face, Ana, Monday),
            here: Pictures.Indexed(face.Photo));

        Assert.Empty(plan.Answers);
        Assert.Single(plan.Held.Answers);
    }

    [Fact]
    public void ABoxThatMovedByAPixelIsStillTheSameFace()
    {
        // What stops a different model version quietly turning every answer in
        // the library into a held one.
        FaceKey theirs = Pictures.Face(@"2019\a.jpg", x: 10, y: 10, size: 40);
        FaceKey mine = Pictures.Face(@"2019\a.jpg", x: 11, y: 10, size: 40);

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").Confirms(theirs, Ana, Monday),
            here: Pictures.Holding(mine));

        Assert.Equal(mine, Assert.Single(plan.Answers).Face);
        Assert.Empty(plan.Held.Answers);
    }

    [Fact]
    public void ADeletedPersonDoesNotComeBack()
    {
        MergePlan plan = Merge(
            mine: new Machine("Dad").Knows(Person(Ana, "Ana") with { DeletedUtc = Monday }),
            theirs: new Machine("Mum").Knows(Person(Ana, "Ana")),
            here: Pictures.Indexed());

        Assert.True(plan.ChangesNothing);
    }

    [Fact]
    public void ADeletedPersonDoesNotWalkBackInFromAThirdMachine()
    {
        // A tombstone is never expired, and this is what that buys: two machines
        // that still hold her cannot outvote the one that deleted her, and the
        // deletion carries on to both.
        MergePlan plan = Merge(
            mine: new Machine("Dad").Knows(Person(Ana, "Ana")),
            here: Pictures.Indexed(),
            theirs:
            [
                new Machine("Mum").Knows(Person(Ana, "Ana") with { DeletedUtc = Monday }),
                new Machine("Ana's laptop").Knows(Person(Ana, "Ana") with { UpdatedUtc = Wednesday }),
            ]);

        Assert.Equal(Monday, Assert.Single(plan.People).DeletedUtc);
    }

    /// <summary>
    /// A name for somebody deleted here is not taken, and does not unseat the
    /// name this library already has on that face.
    /// </summary>
    /// <remarks>
    /// The tombstone wins for the answers about a person as it does for the
    /// person. Taken, the answer was written onto the tombstone - and, being the
    /// later name, withdrew the one this library had, leaving the face named
    /// after nobody anybody can see.
    /// </remarks>
    [Fact]
    public void ANameForSomebodyDeletedHereIsNotTaken()
    {
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad")
                .Knows(Person(Ana, "Ana") with { DeletedUtc = Monday })
                .Knows(Person(Ben, "Ben"))
                .Confirms(face, Ben, Monday),
            theirs: new Machine("Mum").Knows(Person(Ana, "Ana")).Confirms(face, Ana, Wednesday),
            here: Pictures.Holding(face));

        Assert.Empty(plan.Answers);
        Assert.Empty(plan.Withdrawn);
        Assert.True(plan.ChangesNothing);
    }

    /// <summary>
    /// Nor one for somebody another machine has deleted, arriving in the same
    /// sync as the deletion.
    /// </summary>
    [Fact]
    public void ANameForSomebodyDeletedElsewhereIsNotTaken()
    {
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad").Knows(Person(Ana, "Ana")),
            here: Pictures.Holding(face),
            theirs:
            [
                new Machine("Gran").Knows(Person(Ana, "Ana") with { DeletedUtc = Monday }),
                new Machine("Mum").Knows(Person(Ana, "Ana")).Confirms(face, Ana, Wednesday),
            ]);

        Assert.Equal(Monday, Assert.Single(plan.People).DeletedUtc);
        Assert.Empty(plan.Answers);
    }

    /// <summary>
    /// A waiting name for somebody deleted while it waited is settled, not held.
    /// </summary>
    /// <remarks>
    /// Whether or not its face has been found: a tombstone is final, so there is
    /// nothing for the answer to wait for.
    /// </remarks>
    [Fact]
    public void AWaitingNameForSomebodyDeletedHereIsSettled()
    {
        FaceKey found = Pictures.Face(@"2019\a.jpg");
        FaceKey notYet = Pictures.Face(@"2019\b.jpg");
        Guid mum = Guid.NewGuid();

        MergePlan plan = DecisionMerge.Rejoin(
            new Machine("Dad").Knows(Person(Ana, "Ana") with { DeletedUtc = Tuesday }).Set(),
            HeldAnswers.None with
            {
                Answers =
                [
                    new FaceAnswer(found, Ana, AssignmentSource.Confirmed, Monday, mum),
                    new FaceAnswer(notYet, Ana, AssignmentSource.Confirmed, Monday, mum),
                ],
            },
            Pictures.Holding(found));

        Assert.Empty(plan.Answers);
        Assert.Equal(0, plan.Held.Count);
        Assert.Equal(2, plan.Settled.Answers.Count);
    }

    [Fact]
    public void TheLaterRenameStands()
    {
        MergePlan plan = Merge(
            mine: new Machine("Dad").Knows(Person(Ana, "Ana") with { UpdatedUtc = Monday }),
            theirs: new Machine("Mum").Knows(Person(Ana, "Ana Lim") with { UpdatedUtc = Wednesday }),
            here: Pictures.Indexed());

        Assert.Equal("Ana Lim", Assert.Single(plan.People).DisplayName);
    }

    [Fact]
    public void ANameNobodyHasRetypedLosesToOneSomebodyHas()
    {
        // Which is what makes every library that predates sharing behave.
        MergePlan plan = Merge(
            mine: new Machine("Dad").Knows(Person(Ana, "Ana")),
            theirs: new Machine("Mum").Knows(Person(Ana, "Ana Lim") with { UpdatedUtc = Monday }),
            here: Pictures.Indexed());

        Assert.Equal("Ana Lim", Assert.Single(plan.People).DisplayName);
    }

    [Fact]
    public void ARenameDoesNotCostAnybodyTheirBirthYear()
    {
        // Last-write-wins on the whole row would drop it, and nobody types a
        // birth year twice.
        MergePlan plan = Merge(
            mine: new Machine("Dad").Knows(Person(Ana, "Ana") with { BirthYear = 2020 }),
            theirs: new Machine("Mum").Knows(Person(Ana, "Ana Lim") with { UpdatedUtc = Wednesday }),
            here: Pictures.Indexed());

        SharedPerson settled = Assert.Single(plan.People);
        Assert.Equal("Ana Lim", settled.DisplayName);
        Assert.Equal(2020, settled.BirthYear);
    }

    [Fact]
    public void TwoPeopleWithOneNameStayApartAndAreOfferedAsAJoin()
    {
        // Two Anas is a real thing in a family, so the merge notices rather than
        // deciding something only a person can.
        MergePlan plan = Merge(
            mine: new Machine("Dad").Knows(Person(Ana, "Ana")),
            theirs: new Machine("Mum").Knows(Person(Ben, "Ana")),
            here: Pictures.Indexed());

        // Only the one this library had not heard of is added; nothing is joined.
        Assert.Equal(Ben, Assert.Single(plan.People).PublicId);

        PersonJoin offer = Assert.Single(plan.Joins);
        Assert.Equal(JoinEvidence.SameName, offer.Because);
    }

    [Fact]
    public void TwoPeopleWhoLookAlikeAreOfferedEvenUnderDifferentNames()
    {
        MergePlan plan = Merge(
            mine: new Machine("Dad")
                .Knows(Person(Ana, "Ana"))
                .Remembers(Ana, Monday, Wednesday, degrees: 0),
            theirs: new Machine("Mum")
                .Knows(Person(Ben, "Annie"))
                .Remembers(Ben, Monday, Wednesday, degrees: 10),
            here: Pictures.Indexed());

        PersonJoin offer = Assert.Single(plan.Joins);
        Assert.Equal(JoinEvidence.TheyLookAlike, offer.Because);
        Assert.True(offer.Similarity > DecisionMerge.LooksLikeTheSamePerson);
    }

    [Fact]
    public void TwoPeopleWhoMerelyResembleEachOtherAreLeftAlone()
    {
        // Siblings score around 0.55 against each other, which is why the app
        // proposes at half and refuses to suggest a join anywhere near it.
        MergePlan plan = Merge(
            mine: new Machine("Dad")
                .Knows(Person(Ana, "Ana"))
                .Remembers(Ana, Monday, Wednesday, degrees: 0),
            theirs: new Machine("Mum")
                .Knows(Person(Ben, "Ben"))
                .Remembers(Ben, Monday, Wednesday, degrees: 57),
            here: Pictures.Indexed());

        Assert.Empty(plan.Joins);
    }

    [Fact]
    public void APhotographInTwoAlbumsEndsInTheLaterOneAndTheAppSaysWhichItLeft()
    {
        Guid genting = Guid.NewGuid();
        Guid chingay = Guid.NewGuid();
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad")
                .HasAlbum(Pictures.Album(genting, "Genting", Monday))
                .Puts(photo, genting, Monday),
            theirs: new Machine("Mum")
                .HasAlbum(Pictures.Album(chingay, "Chingay", Tuesday))
                .Puts(photo, chingay, Wednesday),
            here: Pictures.Indexed(photo));

        SharedAlbumMove move = Assert.Single(plan.Moves);
        Assert.Equal(genting, move.From);
        Assert.Equal(chingay, move.To);
    }

    /// <summary>
    /// A photograph is not put into an album this library has thrown away.
    /// </summary>
    /// <remarks>
    /// The plan has to be the place this is settled, not the write. A tombstone
    /// is kept for ever and beats any date, so the album is never coming back -
    /// and a move that reaches the database naming it is dropped there with no
    /// row, no waiting answer and no count, then planned again on the next merge
    /// and the one after. An empty plan is the only answer that leaves "merging
    /// twice changes nothing" true.
    /// </remarks>
    [Fact]
    public void APhotographDoesNotJoinAnAlbumThisLibraryHasDeleted()
    {
        Guid genting = Guid.NewGuid();
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad")
                .HasAlbum(Pictures.Album(genting, "Genting", Monday) with { DeletedUtc = Tuesday }),
            theirs: new Machine("Mum")
                .HasAlbum(Pictures.Album(genting, "Genting", Monday))
                .Puts(photo, genting, Wednesday),
            here: Pictures.Indexed(photo));

        Assert.Empty(plan.Moves);
        Assert.True(plan.ChangesNothing, "a move that cannot land must not sit in the plan");
    }

    /// <summary>
    /// A suggestion both machines kept is one album, whatever each calls it.
    /// </summary>
    /// <remarks>
    /// The run of days is the only name two machines share for a proposal - the
    /// pass deletes and reinserts the row, so the identity is this machine's
    /// own. A membership names its album by that identity, so it has to be read
    /// through the same rule the albums themselves are, or every photograph put
    /// into a kept suggestion is dropped on arrival.
    /// </remarks>
    [Fact]
    public void PhotographsPutIntoAKeptSuggestionArriveUnderThisLibrarysOwnName()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        SharedAlbum his = Pictures.Kept(days, "March 2019", Monday);
        SharedAlbum hers = Pictures.Kept(days, "March 2019", Monday);

        // The same days, kept on both machines, and two different identities -
        // which is what a rebuild leaves behind and what this has to see past.
        Assert.NotEqual(his.PublicId, hers.PublicId);

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(his),
            theirs: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Wednesday),
            here: Pictures.Indexed(photo));

        Assert.Equal(his.PublicId, Assert.Single(plan.Moves).To);
    }

    /// <summary>
    /// And a suggestion nobody has kept takes nothing, however many people have
    /// renamed it.
    /// </summary>
    /// <remarks>
    /// The mirror of the rule that stops a proposal's contents being published:
    /// they are derived, and this machine's next rebuild owns them. Taking
    /// another machine's answers into one would put them against that rebuild,
    /// which prunes whatever its own clustering no longer claims. Refused here
    /// rather than written and then quietly undone.
    ///
    /// <para>Renamed on the other machine but never kept there, so nobody
    /// anywhere has said this album is theirs. A keep would be a different
    /// answer - see
    /// <see cref="AKeptSuggestionReachesALibraryThatOnlySuggestsTheSameDays"/>.</para>
    /// </remarks>
    [Fact]
    public void PhotographsAreNotTakenIntoAnAlbumThisLibraryOnlySuggests()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        SharedAlbum hers = Pictures.Proposal(days, "March 2019", Monday);

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(Pictures.Proposal(days, "March 2019")),
            theirs: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Wednesday),
            here: Pictures.Indexed(photo));

        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void ARenameOfAProposedAlbumTravelsOnItsDaysRatherThanItsRow()
    {
        // A proposed row is derived: the pass deletes and reinserts it, so its
        // identity changes for reasons nothing to do with the user. The span of
        // days is what survives that.
        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(Pictures.Proposal("2019-03-03..2019-03-05", "March 2019")),
            theirs: new Machine("Mum")
                .HasAlbum(Pictures.Proposal("2019-03-03..2019-03-05", "Genting Trip", Monday)),
            here: Pictures.Indexed());

        SharedAlbum settled = Assert.Single(plan.Albums);
        Assert.Equal("Genting Trip", settled.Name);
        Assert.Equal("2019-03-03..2019-03-05", settled.ProposalKey);
    }

    [Fact]
    public void AProposalNobodyHasTouchedDoesNotTravelAtAll()
    {
        // The other machine makes its own, and better ones, from the
        // confirmations that came with this.
        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").HasAlbum(Pictures.Proposal("2019-03-03..2019-03-05", "March 2019")),
            here: Pictures.Indexed());

        Assert.Empty(plan.Albums);
    }

    /// <summary>
    /// A suggestion kept on one laptop arrives kept on another that only
    /// suggests the same days, and brings its photographs with it.
    /// </summary>
    /// <remarks>
    /// The common case, and the one that never worked. Both laptops grouped the
    /// same days, so each has its own row for them. Keeping rode along with
    /// whichever side won the name, and with neither renamed the tie went to
    /// this library's own suggestion - which then refused every photograph put
    /// into the kept album, because a suggestion's contents belong to its
    /// rebuild.
    /// </remarks>
    [Fact]
    public void AKeptSuggestionReachesALibraryThatOnlySuggestsTheSameDays()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        SharedAlbum his = Pictures.Proposal(days, "March 2019");
        SharedAlbum hers = Pictures.Kept(days, "March 2019", Monday);

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(his),
            theirs: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Wednesday),
            here: Pictures.Indexed(photo));

        SharedAlbum settled = Assert.Single(plan.Albums);
        Assert.Equal(AlbumOrigin.Accepted, settled.Origin);
        Assert.Equal(Monday, settled.KeptUtc);

        // Under this library's own row, which is the one the write keeps.
        Assert.Equal(his.PublicId, Assert.Single(plan.Moves).To);
    }

    /// <summary>
    /// A keep made by the previous release, which never said when, still beats
    /// a suggestion nobody has answered.
    /// </summary>
    [Fact]
    public void AKeepFromThePreviousReleaseStillBeatsAnUnansweredSuggestion()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        SharedAlbum his = Pictures.Proposal(days, "March 2019");
        var hers = new SharedAlbum(
            Guid.NewGuid(), "March 2019", AlbumOrigin.Accepted, days, null, null);

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(his),
            theirs: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Wednesday),
            here: Pictures.Indexed(photo));

        SharedAlbum settled = Assert.Single(plan.Albums);
        Assert.Equal(AlbumOrigin.Accepted, settled.Origin);
        Assert.Null(settled.KeptUtc);
        Assert.Equal(his.PublicId, Assert.Single(plan.Moves).To);
    }

    /// <summary>
    /// Keeping an album and renaming it are two answers, and each stands on its
    /// own.
    /// </summary>
    /// <remarks>
    /// Whether the album was kept used to ride on whichever side won the name.
    /// So a suggestion renamed on one laptop after it was kept on another came
    /// out renamed and not kept - the later name quietly undoing a keep it
    /// never argued with.
    /// </remarks>
    [Fact]
    public void KeepingAndRenamingAreSettledApart()
    {
        const string days = "2019-03-03..2019-03-05";

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(Pictures.Proposal(days, "Genting", Wednesday)),
            theirs: new Machine("Mum").HasAlbum(Pictures.Kept(days, "March 2019", Monday)),
            here: Pictures.Indexed());

        SharedAlbum settled = Assert.Single(plan.Albums);
        Assert.Equal("Genting", settled.Name);
        Assert.Equal(Wednesday, settled.NamedUtc);
        Assert.Equal(AlbumOrigin.Accepted, settled.Origin);
        Assert.Equal(Monday, settled.KeptUtc);
    }

    /// <summary>
    /// Where nobody typed a name, the album takes the one its keeper sees rather
    /// than this laptop's own guess.
    /// </summary>
    /// <remarks>
    /// Two names the app chose are equally good guesses, and the tie used to go
    /// to whichever sorted later - so a laptop taking in a kept album could
    /// rename it, on the laptop that kept it, to a name nobody there had seen.
    /// </remarks>
    [Fact]
    public void TheKeepersNameBeatsTheOtherLaptopsGuess()
    {
        const string days = "2019-03-03..2019-03-05";

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(Pictures.Proposal(days, "Zz app name")),
            theirs: new Machine("Mum").HasAlbum(Pictures.Kept(days, "March 2019", Monday)),
            here: Pictures.Indexed());

        Assert.Equal("March 2019", Assert.Single(plan.Albums).Name);
    }

    /// <summary>
    /// A dismissal made after the keep takes the kept album away.
    /// </summary>
    /// <remarks>
    /// Whichever answer came last stands, and here it is Dad's. Gran's copy of
    /// the same keep is no later than Mum's, so it changes nothing and its
    /// photographs have nowhere to go. The dismissal is remembered under Dad's
    /// name and moment, because it is what this library goes on to tell the
    /// next laptop.
    /// </remarks>
    [Fact]
    public void ADismissalMadeAfterTheKeepTakesTheKeptAlbumAway()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        Machine dad = new("Dad");

        SharedAlbum hers = Pictures.Kept(days, "March 2019", Monday);
        SharedAlbum grans = Pictures.Kept(days, "March 2019", Monday);

        MergePlan plan = Merge(
            mine: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Monday),
            theirs:
            [
                dad.Dismisses(days, Tuesday),
                new Machine("Gran").HasAlbum(grans).Puts(photo, grans.PublicId, Wednesday),
            ],
            here: Pictures.Indexed(photo));

        Assert.Equal(days, Assert.Single(plan.DismissedHere).ProposalKey);
        Assert.Empty(plan.Albums);
        Assert.Equal(new SharedAlbumDismissal(days, Tuesday, dad.Id), Assert.Single(plan.Dismissals));
        Assert.Empty(plan.Moves);
    }

    /// <summary>And a keep made after the dismissal stands.</summary>
    [Fact]
    public void AKeepMadeAfterTheDismissalStands()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        SharedAlbum hers = Pictures.Kept(days, "March 2019", Tuesday);

        MergePlan plan = Merge(
            mine: new Machine("Dad").Dismisses(days, Monday),
            theirs: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Tuesday),
            here: Pictures.Indexed(photo));

        SharedAlbum settled = Assert.Single(plan.Albums);
        Assert.Equal(AlbumOrigin.Accepted, settled.Origin);
        Assert.Equal(Tuesday, settled.KeptUtc);
        Assert.Equal(hers.PublicId, settled.PublicId);
        Assert.Equal(hers.PublicId, Assert.Single(plan.Moves).To);
        Assert.Empty(plan.DismissedHere);
        Assert.Empty(plan.Dismissals);
    }

    /// <summary>
    /// A keep that says nothing about when it was made loses to a dismissal
    /// that does.
    /// </summary>
    /// <remarks>
    /// The house rule for a moment nobody recorded: it is unknown, so it must
    /// not beat a real one. Every keep made before keeping was dated is one of
    /// these.
    /// </remarks>
    [Fact]
    public void AnUndatedKeepLosesToADismissal()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        SharedAlbum hers = Pictures.Kept(days, "March 2019", keptUtc: null);

        MergePlan plan = Merge(
            mine: new Machine("Dad").Dismisses(days, Monday),
            theirs: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Tuesday),
            here: Pictures.Indexed(photo));

        Assert.Empty(plan.Albums);
        Assert.Empty(plan.Moves);
    }

    /// <summary>
    /// A keep and a dismissal made in the same instant leave the album kept.
    /// </summary>
    /// <remarks>
    /// Somebody has to win the tie, and the same somebody on every laptop. The
    /// keep is the answer that can still be taken back: an album kept by
    /// mistake is one press from gone, and one dismissed by mistake is gone.
    /// </remarks>
    [Fact]
    public void AKeepAndADismissalAtTheSameMomentLeaveItKept()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        SharedAlbum hers = Pictures.Kept(days, "March 2019", Monday);

        MergePlan plan = Merge(
            mine: new Machine("Dad").Dismisses(days, Monday),
            theirs: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Monday),
            here: Pictures.Indexed(photo));

        Assert.Equal(AlbumOrigin.Accepted, Assert.Single(plan.Albums).Origin);
        Assert.Equal(hers.PublicId, Assert.Single(plan.Moves).To);
    }

    /// <summary>Nothing about a keep that lost travels.</summary>
    /// <remarks>
    /// Not its name, and not its shelf. A shelf arriving at a suggestion keeps
    /// it, so a losing keeper's shelf would bring back the very album the
    /// dismissal took away.
    /// </remarks>
    [Fact]
    public void NothingFromAKeepThatLostTravels()
    {
        const string days = "2019-03-03..2019-03-05";

        SharedAlbum hers = Pictures.Kept(days, "Genting", Monday, namedUtc: Monday) with
        {
            Shelf = Guid.NewGuid(),
            ShelvedUtc = Monday,
        };

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(Pictures.Proposal(days, "March 2019")),
            theirs:
            [
                new Machine("Mum").HasAlbum(hers),
                new Machine("Gran").Dismisses(days, Tuesday),
            ],
            here: Pictures.Indexed());

        Assert.Empty(plan.Albums);
    }

    /// <summary>
    /// Every laptop settles on the same answer whatever order it reads the
    /// others in.
    /// </summary>
    /// <remarks>
    /// Keeps, dismissals and their dates are decided from every laptop's answer
    /// at once rather than two at a time, so no reading order can carry one
    /// laptop's date onto another's album.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheHouseSettlesTheSameInEveryReadingOrder(bool dismissedLast)
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey a = Pictures.Photo(@"2019\a.jpg");
        AssetKey b = Pictures.Photo(@"2019\b.jpg");

        // Kept, dismissed, kept again - or kept twice and then dismissed.
        DateTime granKept = dismissedLast ? Tuesday : Wednesday;
        DateTime dadDismissed = dismissedLast ? Wednesday : Tuesday;

        SharedAlbum hers = Pictures.Kept(days, "March 2019", Monday);
        SharedAlbum grans = Pictures.Kept(days, "March 2019", granKept);

        Machine[] house =
        [
            new Machine("Mum").HasAlbum(hers).Puts(a, hers.PublicId, Monday),
            new Machine("Dad").Dismisses(days, dadDismissed),
            new Machine("Gran").HasAlbum(grans).Puts(b, grans.PublicId, granKept),
        ];

        Machine mine = new Machine("Ana's laptop").HasAlbum(Pictures.Proposal(days, "March 2019"));

        List<MergePlan> plans =
        [
            .. Orders(house).Select(order => Merge(mine, order, Pictures.Indexed(a, b))),
        ];

        Assert.Equal(6, plans.Count);

        foreach (MergePlan plan in plans)
        {
            Assert.Equal(plans[0].Albums, plan.Albums);
            Assert.Equal(plans[0].Dismissals, plan.Dismissals);
            Assert.Equal(plans[0].DismissedHere, plan.DismissedHere);
            Assert.Equal(Sorted(plans[0].Moves), Sorted(plan.Moves));
        }

        Assert.Empty(plans[0].DismissedHere);

        if (dismissedLast)
        {
            Assert.Empty(plans[0].Albums);
            Assert.Empty(plans[0].Moves);
        }
        else
        {
            SharedAlbum settled = Assert.Single(plans[0].Albums);
            Assert.Equal(AlbumOrigin.Accepted, settled.Origin);
            Assert.Equal(Wednesday, settled.KeptUtc);
        }
    }

    /// <summary>
    /// Merging again after a keep or a dismissal has landed changes nothing.
    /// </summary>
    /// <remarks>
    /// Three ways in: a suggestion taken in as kept, a kept album taken away by
    /// a later dismissal, and a kept album brought back by a later keep. Each
    /// is applied the way the write applies it, and the next merge must find
    /// nothing left to do.
    /// </remarks>
    [Fact]
    public void MergingTwiceAfterAKeepOrADismissalChangesNothing()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        LibraryContents here = Pictures.Indexed(photo);

        SharedAlbum his = Pictures.Proposal(days, "March 2019");
        SharedAlbum hers = Pictures.Kept(days, "March 2019", Monday);

        SettlesInOneMerge(
            new Machine("Dad").HasAlbum(his).Set(),
            [new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Wednesday).Set()],
            here);

        SharedAlbum mums = Pictures.Kept(days, "March 2019", Monday);
        SharedAlbum grans = Pictures.Kept(days, "March 2019", Monday);

        SettlesInOneMerge(
            new Machine("Mum").HasAlbum(mums).Puts(photo, mums.PublicId, Monday).Set(),
            [
                new Machine("Dad").Dismisses(days, Tuesday).Set(),
                new Machine("Gran").HasAlbum(grans).Puts(photo, grans.PublicId, Wednesday).Set(),
            ],
            here);

        SharedAlbum keptLater = Pictures.Kept(days, "March 2019", Tuesday);

        SettlesInOneMerge(
            new Machine("Dad").Dismisses(days, Monday).Set(),
            [new Machine("Mum").HasAlbum(keptLater).Puts(photo, keptLater.PublicId, Tuesday).Set()],
            here);
    }

    /// <summary>
    /// Only an album the write can put somewhere is planned.
    /// </summary>
    /// <remarks>
    /// A suggestion renamed elsewhere, for days this library has not grouped,
    /// has nowhere to land: only a keep or a deletion makes a row from a merge.
    /// It used to be planned on every merge and skipped by the write every
    /// time. A deletion with no row here is written - it is what stops this
    /// library suggesting days somebody threw away - so that one must still be
    /// planned.
    /// </remarks>
    [Fact]
    public void OnlyWhatCanBeWrittenIsPlanned()
    {
        const string days = "2019-03-03..2019-03-05";

        MergePlan renamed = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").HasAlbum(Pictures.Proposal(days, "Genting", Monday)),
            here: Pictures.Indexed());

        Assert.Empty(renamed.Albums);

        MergePlan deleted = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").HasAlbum(
                Pictures.Kept(days, "March 2019", Monday) with { DeletedUtc = Tuesday }),
            here: Pictures.Indexed());

        Assert.Equal(Tuesday, Assert.Single(deleted.Albums).DeletedUtc);
    }

    /// <summary>
    /// A cover chosen on a kept album beats one chosen in the same instant on a
    /// suggestion.
    /// </summary>
    /// <remarks>
    /// The tie used to fall to the photograph's own key, which is the right
    /// answer only between two albums with the same standing. Between a kept
    /// album and a suggestion it hands the picture to whichever sorts later.
    /// </remarks>
    [Fact]
    public void AChosenCoverOnAKeptAlbumBeatsTheSameMomentOnASuggestion()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey his = Pictures.Photo(@"2019\z.jpg");
        AssetKey hers = Pictures.Photo(@"2019\a.jpg");

        // His sorts later, which is the tie-break that used to decide it.
        Assert.True(string.CompareOrdinal(his.ToString(), hers.ToString()) > 0);

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(
                Pictures.Proposal(days, "March 2019") with { Cover = his, CoverChosenUtc = Monday }),
            theirs: new Machine("Mum").HasAlbum(
                Pictures.Kept(days, "March 2019", Monday) with { Cover = hers, CoverChosenUtc = Monday }),
            here: Pictures.Indexed(his, hers));

        Assert.Equal(hers, Assert.Single(plan.Albums).Cover);
    }

    /// <summary>
    /// A keep or a dismissal dated too far ahead is refused, as any other
    /// answer is.
    /// </summary>
    /// <remarks>
    /// Both now compete on their dates, so a clock a year ahead would win every
    /// argument about keeping through a door the guard had not shut.
    /// </remarks>
    [Fact]
    public void AKeepOrADismissalDatedTooFarAheadIsRefused()
    {
        const string days = "2019-03-03..2019-03-05";

        MergePlan kept = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").HasAlbum(Pictures.Kept(days, "March 2019", Now.AddDays(2))),
            here: Pictures.Indexed());

        Assert.Equal(RefusalReason.ClockTooFarAhead, Assert.Single(kept.Refused).Reason);

        MergePlan dismissed = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").Dismisses(days, Now.AddDays(2)),
            here: Pictures.Indexed());

        Assert.Equal(RefusalReason.ClockTooFarAhead, Assert.Single(dismissed.Refused).Reason);
    }

    /// <summary>
    /// Nothing joins a kept album on the merge that throws it away.
    /// </summary>
    /// <remarks>
    /// The album is still live here when the merge plans, but the same merge
    /// turns it into a tombstone - and the write skips a move into a tombstone,
    /// so planning one is a move that never lands and is planned again.
    /// </remarks>
    [Fact]
    public void NothingJoinsAKeptAlbumOnTheMergeThatThrowsItAway()
    {
        const string days = "2019-03-03..2019-03-05";
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        SharedAlbum dads = Pictures.Kept(days, "March 2019", Monday);

        MergePlan plan = Merge(
            mine: new Machine("Mum").HasAlbum(Pictures.Kept(days, "March 2019", Monday)),
            theirs:
            [
                new Machine("Gran").HasAlbum(
                    Pictures.Kept(days, "March 2019", Monday) with { DeletedUtc = Tuesday }),
                new Machine("Dad").HasAlbum(dads).Puts(photo, dads.PublicId, Wednesday),
            ],
            here: Pictures.Indexed(photo));

        Assert.Empty(plan.Moves);
    }

    /// <summary>
    /// A photograph filed elsewhere lands on the same merge that takes its kept
    /// album away.
    /// </summary>
    /// <remarks>
    /// The membership in the album being removed is later than the filing, so
    /// it used to beat it - and then went with the album, leaving the
    /// photograph in nothing until a second merge.
    /// </remarks>
    [Fact]
    public void AFilingElsewhereLandsOnTheMergeThatTakesAKeptAlbumAway()
    {
        const string days = "2019-03-03..2019-03-05";
        DateTime sunday = Monday.AddDays(-1);
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        Guid best = Guid.NewGuid();

        SharedAlbum hers = Pictures.Kept(days, "March 2019", Monday);

        MergePlan plan = Merge(
            mine: new Machine("Mum").HasAlbum(hers).Puts(photo, hers.PublicId, Monday),
            theirs: new Machine("Dad")
                .Dismisses(days, Tuesday)
                .HasAlbum(Pictures.Album(best, "Best shots", sunday))
                .Puts(photo, best, sunday),
            here: Pictures.Indexed(photo));

        Assert.Single(plan.DismissedHere);
        Assert.Equal(best, Assert.Single(plan.Moves).To);
    }

    /// <summary>
    /// The picture somebody chose crosses, and on its own date.
    /// </summary>
    /// <remarks>
    /// Not carried by whoever won the name, for the reason the shelf is not
    /// either: renaming an album and choosing the picture it shows are two
    /// decisions, made at two moments, and settling both on the name's date
    /// hands every argument about pictures to whoever typed last.
    /// </remarks>
    [Fact]
    public void TheCoverChosenLastIsTheOneThatTravels()
    {
        Guid album = Guid.NewGuid();
        AssetKey hers = Pictures.Photo(@"2019\hers.jpg");
        AssetKey his = Pictures.Photo(@"2019\his.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(
                Pictures.Album(album, "Bali", Tuesday) with { Cover = his, CoverChosenUtc = Monday }),
            theirs: new Machine("Mum").HasAlbum(
                Pictures.Album(album, "Bali", Monday) with { Cover = hers, CoverChosenUtc = Tuesday }),
            here: Pictures.Indexed(hers, his));

        SharedAlbum settled = Assert.Single(plan.Albums);

        // His name, hers the picture - which is the whole point of the two
        // dates being separate.
        Assert.Equal(hers, settled.Cover);
        Assert.Equal(Tuesday, settled.CoverChosenUtc);
    }

    /// <summary>
    /// A cover about a photograph this library has not indexed is not proposed
    /// at all.
    /// </summary>
    /// <remarks>
    /// Left out rather than held. A decision set is whole state, so the machine
    /// that chose it goes on saying so and it lands by itself on the first merge
    /// after the scan that finds the picture. What it must not do meanwhile is
    /// sit in the plan: an album that differs for ever is "merging twice changes
    /// nothing" quietly ceasing to be true, on the one album nobody could see a
    /// change in.
    /// </remarks>
    [Fact]
    public void ACoverForAPhotographThisLibraryHasNotGotIsNotProposedYet()
    {
        Guid album = Guid.NewGuid();

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(Pictures.Album(album, "Bali", Monday)),
            theirs: new Machine("Mum").HasAlbum(
                Pictures.Album(album, "Bali", Monday) with
                {
                    Cover = Pictures.Photo(@"2019\hers.jpg"),
                    CoverChosenUtc = Tuesday,
                }),
            here: Pictures.Indexed());

        Assert.Empty(plan.Albums);
    }

    /// <summary>And it is proposed the moment the scan finds that photograph.</summary>
    [Fact]
    public void ACoverLandsOnceThePhotographItNamesHasBeenIndexed()
    {
        Guid album = Guid.NewGuid();
        AssetKey hers = Pictures.Photo(@"2019\hers.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad").HasAlbum(Pictures.Album(album, "Bali", Monday)),
            theirs: new Machine("Mum").HasAlbum(
                Pictures.Album(album, "Bali", Monday) with { Cover = hers, CoverChosenUtc = Tuesday }),
            here: Pictures.Indexed(hers));

        Assert.Equal(hers, Assert.Single(plan.Albums).Cover);
    }

    [Fact]
    public void ProposalsAreNotPublished()
    {
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        DecisionSet published = new Machine("Mum")
            .Confirms(face, Ana, Monday)
            .Proposes(Pictures.Face(@"2019\b.jpg"), Ben, Monday)
            .Set()
            .WithoutProposals();

        Assert.Equal(AssignmentSource.Confirmed, Assert.Single(published.Answers).Source);
    }

    [Fact]
    public void ATurnedPhotographSettlesOnTheLaterTurn()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad").Turns(photo, 90, Monday),
            theirs: new Machine("Mum").Turns(photo, 270, Wednesday),
            here: Pictures.Indexed(photo));

        Assert.Equal(270, Assert.Single(plan.Turns).Rotation);
    }

    [Fact]
    public void ATurnForAPhotographThisLibraryHasNotIndexedWaits()
    {
        AssetKey photo = Pictures.Photo(@"2026 Phone Dump\b.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").Turns(photo, 90, Monday),
            here: Pictures.Indexed());

        Assert.Empty(plan.Turns);
        Assert.Equal(photo, Assert.Single(plan.Held.Turns).Photo);
    }

    /// <summary>
    /// A waiting move into an album this library has no row for is not planned.
    /// </summary>
    /// <remarks>
    /// The one way a sweep let an answer go without writing it. Nothing arrives
    /// with a held answer but the answer itself, so the album it names is one of
    /// this library's rows or it is nothing - and a plan that moved the
    /// photograph into nothing was skipped by the write without a word, after
    /// which the held row was forgotten as though it had landed.
    /// </remarks>
    [Fact]
    public void AWaitingMoveIntoAnAlbumThisLibraryHasNotGotIsNotPlanned()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        MergePlan plan = DecisionMerge.Rejoin(
            new Machine("Dad").Set(),
            Waiting(new SharedAlbumMembership(photo, Guid.NewGuid(), Monday, Guid.NewGuid())),
            Pictures.Indexed(photo));

        Assert.Empty(plan.Moves);

        // Kept, and kept apart from what is waiting for its photograph: the
        // photograph is here, and nothing about it has settled the answer.
        Assert.Equal(photo, Assert.Single(plan.Unplaced).Photo);
        Assert.Equal(0, plan.Held.Count);
        Assert.Equal(0, plan.Settled.Count);
    }

    /// <summary>
    /// And it is settled once this library holds the same answer, whatever it
    /// calls the album.
    /// </summary>
    /// <remarks>
    /// Judged on when it was decided and by whom, not on the album it names. The
    /// held row names the album as its author's machine did; this library's own
    /// membership names its own row - so the two are never equal as records, and
    /// a rule that compared them would keep the row for ever.
    /// </remarks>
    [Fact]
    public void AWaitingMoveIsSettledOnceThisLibraryHoldsTheSameAnswerUnderAnotherName()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        Guid ours = Guid.NewGuid();
        Machine mum = new("Mum");

        MergePlan plan = DecisionMerge.Rejoin(
            new Machine("Dad")
                .HasAlbum(Pictures.Album(ours, "Genting", Monday))
                .Set() with
            {
                Memberships = [new SharedAlbumMembership(photo, ours, Monday, mum.Id)],
            },
            Waiting(new SharedAlbumMembership(photo, Guid.NewGuid(), Monday, mum.Id)),
            Pictures.Indexed(photo));

        Assert.Empty(plan.Moves);
        Assert.Empty(plan.Unplaced);
        Assert.Single(plan.Settled.Memberships);
    }

    /// <summary>
    /// A waiting move into an album this library threw away is settled, whether
    /// or not its photograph has arrived.
    /// </summary>
    /// <remarks>
    /// A tombstone is final, so there is nothing for either answer to wait for.
    /// Settled rather than applied: the photograph went nowhere.
    /// </remarks>
    [Fact]
    public void AWaitingMoveIntoAnAlbumThisLibraryThrewAwayIsSettled()
    {
        Guid genting = Guid.NewGuid();
        AssetKey here = Pictures.Photo(@"2019\a.jpg");
        AssetKey notYet = Pictures.Photo(@"2019\b.jpg");

        MergePlan plan = DecisionMerge.Rejoin(
            new Machine("Dad")
                .HasAlbum(Pictures.Album(genting, "Genting", Monday) with { DeletedUtc = Tuesday })
                .Set(),
            Waiting(
                new SharedAlbumMembership(here, genting, Monday, Guid.NewGuid()),
                new SharedAlbumMembership(notYet, genting, Monday, Guid.NewGuid())),
            Pictures.Indexed(here));

        Assert.Empty(plan.Moves);
        Assert.Equal(0, plan.Held.Count);
        Assert.Empty(plan.Unplaced);
        Assert.Equal(2, plan.Settled.Count);
    }

    /// <summary>
    /// A waiting move into a suggestion nobody here has kept stays waiting.
    /// </summary>
    /// <remarks>
    /// The opposite of what a merge does with the same answer, and on purpose. A
    /// merge refuses it, because the machine that sent it sends it again on
    /// every sync; nothing sends a held answer again, so refusing it here would
    /// be throwing it away - and the day somebody keeps that suggestion, it can
    /// land.
    /// </remarks>
    [Fact]
    public void AWaitingMoveIntoASuggestionThisLibraryHasNotKeptStaysWaiting()
    {
        SharedAlbum suggested = Pictures.Proposal("2019-03-03..2019-03-05", "March 2019");
        AssetKey here = Pictures.Photo(@"2019\a.jpg");
        AssetKey notYet = Pictures.Photo(@"2019\b.jpg");

        MergePlan plan = DecisionMerge.Rejoin(
            new Machine("Dad").HasAlbum(suggested).Set(),
            Waiting(
                new SharedAlbumMembership(here, suggested.PublicId, Monday, Guid.NewGuid()),
                new SharedAlbumMembership(notYet, suggested.PublicId, Monday, Guid.NewGuid())),
            Pictures.Indexed(here));

        Assert.Empty(plan.Moves);
        Assert.Equal(here, Assert.Single(plan.Unplaced).Photo);
        Assert.Equal(notYet, Assert.Single(plan.Held.Memberships).Photo);
        Assert.Equal(0, plan.Settled.Count);
    }

    /// <summary>
    /// A waiting answer this library already holds is settled, not landed.
    /// </summary>
    [Fact]
    public void AWaitingAnswerThisLibraryAlreadyHoldsIsSettledRatherThanLanded()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        Machine mum = new("Mum");
        var turn = new PhotoTurn(photo, 90, Monday, mum.Id);

        MergePlan plan = DecisionMerge.Rejoin(
            new Machine("Dad").Set() with { Turns = [turn] },
            HeldAnswers.None with { Turns = [turn] },
            Pictures.Indexed(photo));

        Assert.Empty(plan.Turns);
        Assert.Equal(turn, Assert.Single(plan.Settled.Turns));
        Assert.True(plan.ChangesNothing, "an answer already true here writes nothing");
    }

    /// <summary>
    /// Two waiting moves for one photograph are one move, and the other is
    /// settled by it.
    /// </summary>
    [Fact]
    public void TwoWaitingMovesForOnePhotographAreOneMoveAndOneSettled()
    {
        Guid genting = Guid.NewGuid();
        Guid chingay = Guid.NewGuid();
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        Guid mum = Guid.NewGuid();

        var earlier = new SharedAlbumMembership(photo, genting, Monday, mum);
        var later = new SharedAlbumMembership(photo, chingay, Tuesday, mum);

        MergePlan plan = DecisionMerge.Rejoin(
            new Machine("Dad")
                .HasAlbum(Pictures.Album(genting, "Genting", Monday))
                .HasAlbum(Pictures.Album(chingay, "Chingay", Monday))
                .Set(),
            Waiting(later, earlier),
            Pictures.Indexed(photo));

        Assert.Equal(chingay, Assert.Single(plan.Moves).To);
        Assert.Equal(earlier, Assert.Single(plan.Settled.Memberships));
    }

    [Fact]
    public void AMachineWithNoSourceInCommonIsToldSoRatherThanReportedAsASuccess()
    {
        Machine stranger = new("Aunt", sources: Guid.NewGuid());

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: stranger.Knows(Person(Ana, "Ana")),
            here: Pictures.Indexed());

        Assert.True(plan.ChangesNothing);
        Assert.Equal(RefusalReason.NoSourceInCommon, Assert.Single(plan.Refused).Reason);
    }

    [Fact]
    public void SourcesThatOverlapInPartShareTheCommonOnesAndNothingElse()
    {
        // Scoped per source rather than per library, because refusing two
        // machines whose lists differ at all would mean a family member who adds
        // their own phone-dump folder can no longer share anything.
        Guid theirsAlone = Guid.NewGuid();
        FaceKey shared = Pictures.Face(@"2019\a.jpg");
        FaceKey theirsOnly = new(Pictures.Photo(@"phone\b.jpg", theirsAlone), shared.Bounds);

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum", sources: [Machine.Share, theirsAlone])
                .Confirms(shared, Ana, Monday)
                .Confirms(theirsOnly, Ana, Monday),
            here: Pictures.Holding(shared));

        Assert.Empty(plan.Refused);
        Assert.Equal(shared, Assert.Single(plan.Answers).Face);

        // Carried, not dropped: they cost nothing and come good the day that
        // folder reaches the shared drive.
        Assert.Equal(theirsOnly, Assert.Single(plan.Held.Answers).Face);
    }

    [Fact]
    public void AMachineWhoseClockIsFarAheadIsRefusedByNameAndBySkew()
    {
        // Otherwise it wins for ever: every answer it makes overrides everybody
        // else's on every merge, including answers made long afterwards, and
        // nothing about the result looks broken.
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").Confirms(face, Ana, Now.AddYears(1)),
            here: Pictures.Holding(face));

        RefusedSet refused = Assert.Single(plan.Refused);
        Assert.Equal(RefusalReason.ClockTooFarAhead, refused.Reason);
        Assert.Equal("Mum", refused.Machine.Name);
        Assert.Contains("1 year ahead", refused.Detail);
        Assert.True(plan.ChangesNothing);
    }

    [Fact]
    public void AClockAFewSecondsOutIsNotAProblem()
    {
        // Laptops on NTP agree to the second and must never be refused for it.
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").Confirms(face, Ana, Now.AddSeconds(90)),
            here: Pictures.Holding(face));

        Assert.Empty(plan.Refused);
        Assert.Single(plan.Answers);
    }

    [Fact]
    public void APayloadFromANewerReleaseIsRefusedWholeRatherThanPartlyApplied()
    {
        FaceKey face = Pictures.Face(@"2019\a.jpg");

        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum", schemaVersion: 2).Confirms(face, Ana, Monday),
            here: Pictures.Holding(face));

        Assert.Equal(RefusalReason.SchemaTooNew, Assert.Single(plan.Refused).Reason);
        Assert.True(plan.ChangesNothing);
    }

    [Fact]
    public void AnasLaptopHoldsMumsAnswersBecauseDadPassedThemOn()
    {
        // Forwarding what you were told is what makes three machines converge
        // with no machinery for it, and it is safe because the answer keeps who
        // decided it rather than who handed it over.
        FaceKey face = Pictures.Face(@"2019\a.jpg");
        Machine mum = new("Mum");
        DecisionSet fromDad = new Machine("Dad")
            .Knows(Person(Ana, "Ana"))
            .Set() with
        {
            Answers = [new FaceAnswer(face, Ana, AssignmentSource.Confirmed, Monday, mum.Id)],
        };

        MergePlan plan = DecisionMerge.Merge(
            new Machine("Ana's laptop").Set(), [fromDad], Pictures.Holding(face), Now);

        Assert.Equal(mum.Id, Assert.Single(plan.Answers).DecidedBy);
    }

    [Fact]
    public void ThreeMachinesConvergeWhateverOrderTheyAreReadIn()
    {
        // Two answers stamped the same second have to be settled the same way on
        // every laptop, or each keeps whichever it happened to read last and
        // every merge undoes the one before.
        FaceKey face = Pictures.Face(@"2019\a.jpg");
        Machine mum = new("Mum", new Guid("11111111-0000-4000-8000-000000000001"));
        Machine dad = new("Dad", new Guid("22222222-0000-4000-8000-000000000002"));

        mum.Confirms(face, Ana, Monday);
        dad.Confirms(face, Ben, Monday);

        MergePlan oneWay = DecisionMerge.Merge(
            new Machine("Ana's laptop").Set(),
            [mum.Set(), dad.Set()],
            Pictures.Holding(face),
            Now);

        MergePlan theOther = DecisionMerge.Merge(
            new Machine("Ana's laptop").Set(),
            [dad.Set(), mum.Set()],
            Pictures.Holding(face),
            Now);

        Assert.Equal(
            Assert.Single(oneWay.Answers).Person,
            Assert.Single(theOther.Answers).Person);

        // And it is the machine that settles it, not the order, so the third
        // laptop agrees with both of them rather than with whichever it read
        // last.
        Assert.Equal(Ben, Assert.Single(oneWay.Answers).Person);
    }

    [Fact]
    public void ACentroidIsTakenOnlyWhereThisLibraryCannotBuildItsOwn()
    {
        // A seed, not a fact: where this machine has confirmed faces of its own
        // in that stretch they made a better centroid than an average of
        // somebody else's.
        MergePlan wanted = Merge(
            mine: new Machine("Dad"),
            theirs: new Machine("Mum").Remembers(Ana, Monday, Wednesday, degrees: 0),
            here: Pictures.Indexed());

        Assert.Single(wanted.Eras);

        MergePlan declined = Merge(
            mine: new Machine("Dad").Remembers(Ana, Monday, Wednesday, degrees: 5),
            theirs: new Machine("Mum").Remembers(Ana, Monday, Wednesday, degrees: 0),
            here: Pictures.Indexed());

        Assert.Empty(declined.Eras);
    }

    [Fact]
    public void ACentroidIsTakenOnceHoweverManyMachinesOfferOne()
    {
        // Three machines with the same person over the same years is the same
        // answer three times, not a richer one, and the next rebuild would have
        // to choose between them for no reason.
        MergePlan plan = Merge(
            mine: new Machine("Dad"),
            here: Pictures.Indexed(),
            theirs:
            [
                new Machine("Mum").Remembers(Ana, Monday, Wednesday, degrees: 0),
                new Machine("Ana's laptop").Remembers(Ana, Monday, Wednesday, degrees: 3),
            ]);

        Assert.Single(plan.Eras);
    }

    [Fact]
    public void AMachineDoesNotMergeItsOwnFileBackIn()
    {
        // Everybody's answers sit in one folder, this machine's among them.
        Machine dad = new("Dad");
        dad.Confirms(Pictures.Face(@"2019\a.jpg"), Ana, Monday);

        MergePlan plan = DecisionMerge.Merge(
            dad.Set(), [dad.Set()], Pictures.Holding(Pictures.Face(@"2019\a.jpg")), Now);

        Assert.True(plan.ChangesNothing);
        Assert.Empty(plan.Refused);
    }

    [Fact]
    public void NothingInADecisionSetSaysHowAnybodyLooksAtTheirPictures()
    {
        // The cheapest possible guarantee that the theme, the cell size, the sort
        // order and the nav state never travel: there is nowhere to put them.
        string[] fields = [.. typeof(DecisionSet)
            .GetProperties()
            .Select(property => property.Name)];

        Assert.DoesNotContain("Theme", fields);
        Assert.DoesNotContain("GalleryCellSize", fields);
        Assert.DoesNotContain("GallerySortOrder", fields);
        Assert.DoesNotContain("NavigationCollapsed", fields);
    }

    private static SharedPerson Person(Guid id, string name) =>
        new(id, name, BirthYear: null, UpdatedUtc: null, DeletedUtc: null);

    /// <summary>Album memberships parked on this library, waiting for a sweep.</summary>
    private static HeldAnswers Waiting(params SharedAlbumMembership[] memberships) =>
        HeldAnswers.None with { Memberships = memberships };

    private static MergePlan Merge(Machine mine, Machine theirs, LibraryContents here) =>
        DecisionMerge.Merge(mine.Set(), [theirs.Set()], here, Now);

    private static MergePlan Merge(
        Machine mine, IReadOnlyList<Machine> theirs, LibraryContents here) =>
        DecisionMerge.Merge(mine.Set(), [.. theirs.Select(m => m.Set())], here, Now);

    /// <summary>
    /// Asserts that a merge which changes something leaves nothing for the next
    /// one, once it has been carried out.
    /// </summary>
    private static void SettlesInOneMerge(
        DecisionSet mine, IReadOnlyList<DecisionSet> theirs, LibraryContents here)
    {
        MergePlan first = DecisionMerge.Merge(mine, theirs, here, Now);
        Assert.False(first.ChangesNothing, "the first merge should have had something to do");

        MergePlan second = DecisionMerge.Merge(mine.Apply(first), theirs, here, Now);
        Assert.True(second.ChangesNothing, "the second merge should find nothing to do");
    }

    /// <summary>Every order the same machines can be read in.</summary>
    private static IEnumerable<IReadOnlyList<T>> Orders<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }

        for (int first = 0; first < items.Count; first++)
        {
            List<T> rest = [.. items.Where((_, index) => index != first)];

            foreach (IReadOnlyList<T> tail in Orders(rest))
            {
                yield return [items[first], .. tail];
            }
        }
    }

    /// <summary>Moves in an order that does not depend on the order they were read in.</summary>
    private static List<SharedAlbumMove> Sorted(IEnumerable<SharedAlbumMove> moves) =>
        [.. moves.OrderBy(move => move.Photo.ToString(), StringComparer.Ordinal)];
}
