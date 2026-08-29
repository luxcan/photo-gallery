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

        AlbumMove move = Assert.Single(plan.Moves);
        Assert.Equal(genting, move.From);
        Assert.Equal(chingay, move.To);
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

    private static MergePlan Merge(Machine mine, Machine theirs, LibraryContents here) =>
        DecisionMerge.Merge(mine.Set(), [theirs.Set()], here, Now);

    private static MergePlan Merge(
        Machine mine, IReadOnlyList<Machine> theirs, LibraryContents here) =>
        DecisionMerge.Merge(mine.Set(), [.. theirs.Select(m => m.Set())], here, Now);
}
