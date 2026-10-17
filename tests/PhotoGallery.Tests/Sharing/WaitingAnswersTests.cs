using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Why an answer is waiting, which is the whole of what the screen can honestly
/// say about it.
/// </summary>
/// <remarks>
/// There was one number here and one sentence describing it: "N answers are
/// waiting for photos this library has not indexed yet - scanning will bring
/// them in". On a real library of sixteen thousand photographs that sentence was
/// false for every one of the thirty-eight it counted. Thirty-seven named files
/// that had been renamed where they live, so no scan will ever find them under
/// those names again; the last was a photograph sitting right there, waiting on
/// a face this machine has not found. The count could not fall, so the caution
/// banner showed for ever, asking for a scan that could not help.
///
/// <para>Three groups, because there are three answers to "what would have to
/// happen" and only one of them is anybody's to do.</para>
/// </remarks>
public sealed class WaitingAnswersTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);
    private static readonly FaceBounds Elsewhere = new(500, 500, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task AnAnswerForAPhotographThatIsHereIsWaitingOnAFace()
    {
        // Dad has the picture and has never run the face pass over it, so
        // nothing about the photograph needs to change - only this machine's own
        // work has not caught up.
        Mum.Answer(
            Mum.Face(Mum.Photo(@"2019\a.jpg"), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        Dad.Photo(@"2019\a.jpg");

        HeldStanding held = await MergedAsync();

        Assert.Equal(1, held.Faces.Answers);
        Assert.Equal(1, held.Faces.Photos);
        Assert.Equal(0, held.Missing.Answers);
        Assert.Equal(0, held.Elsewhere.Answers);
    }

    /// <summary>
    /// The case that made the old sentence a lie on a real library.
    /// </summary>
    /// <remarks>
    /// The photograph is indexed, the face pass <em>has</em> run, and this
    /// machine's detector simply drew no box where the other machine drew one.
    /// No amount of scanning changes that, and the copy must not imply it does -
    /// but it is still a face this library is waiting on rather than a
    /// photograph it is missing, so it belongs in this group and not another.
    /// </remarks>
    [Fact]
    public async Task AFaceThisMachineFoundSomewhereElseInThePictureStillCountsAsAFace()
    {
        Mum.Answer(
            Mum.Face(Mum.Photo(@"2019\a.jpg"), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        Dad.Face(Dad.Photo(@"2019\a.jpg"), Elsewhere);

        HeldStanding held = await MergedAsync();

        Assert.Equal(1, held.Faces.Answers);
        Assert.Equal(0, held.Missing.Answers);
    }

    /// <summary>
    /// A turn waiting for its small copy is not described as waiting on a face.
    /// </summary>
    /// <remarks>
    /// The photograph is here and nothing about it is a face: the turn waits
    /// because this computer has no picture of it to turn yet. Counting every
    /// row on a photograph that is here as a face put this one under a sentence
    /// about the face pass, which will never bring it in.
    /// </remarks>
    [Fact]
    public async Task ATurnWaitingForItsSmallCopyIsNotWaitingOnAFace()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");

        Asset turned = Mum.Db.Assets.Single();
        turned.Rotation = 90;
        turned.RotatedUtc = Monday;
        turned.RotatedBy = Mum.MachineId;
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();

        // Dad has the photograph and no small copy of it, so the turn waits.
        Dad.Photo(@"2019\a.jpg");

        HeldStanding held = await MergedAsync();

        Assert.Single(Dad.Db.HeldDecisions, h => h.Kind == HeldDecisionKind.Turn);
        Assert.Equal(0, held.Faces.Answers);
        Assert.Equal(1, held.Here.Answers);
        Assert.Equal(1, held.Here.Photos);
        Assert.Equal(1, held.Answers);
    }

    /// <summary>
    /// Nor is a photograph waiting for an album this computer does not have.
    /// </summary>
    [Fact]
    public async Task AMoveWaitingForAnAlbumIsNotWaitingOnAFace()
    {
        await Dad.Writing.ApplyAsync(MergePlan.Nothing with
        {
            Held = HeldAnswers.None with
            {
                Memberships =
                [
                    new SharedAlbumMembership(
                        new AssetKey(Dad.SharedSourceId, @"2019\a.jpg"),
                        Guid.NewGuid(),
                        Monday,
                        Mum.MachineId),
                ],
            },
        });

        Dad.Photo(@"2019\a.jpg");
        await Dad.Waiting.HandleAsync();

        HeldStanding held = await Dad.Decisions.HeldStandingAsync();

        Assert.Equal(0, held.Faces.Answers);
        Assert.Equal(1, held.Here.Answers);
        Assert.Equal(1, held.Answers);
    }

    [Fact]
    public async Task AnAnswerForAPhotographThisLibraryHasNotIndexedIsMissing()
    {
        // Same folder, and Dad has nothing under that name in it. Either it has
        // not arrived or it has been renamed, and which of the two cannot be
        // known from here - which is exactly why the sentence is conditional.
        Mum.Answer(
            Mum.Face(Mum.Photo(@"2019\a.jpg"), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        HeldStanding held = await MergedAsync();

        Assert.Equal(1, held.Missing.Answers);
        Assert.Equal(1, held.Missing.Photos);
        Assert.Equal(0, held.Faces.Answers);
        Assert.Equal(0, held.Elsewhere.Answers);
    }

    [Fact]
    public async Task AnAnswerForAFolderThisLibraryDoesNotHaveIsElsewhere()
    {
        // Somebody's phone dump, on their own laptop and nowhere else.
        PhotoSource phone = Mum.Folder(@"C:\Users\Mum\Pictures");

        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\b.jpg", into: phone), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        HeldStanding held = await MergedAsync();

        Assert.Equal(1, held.Elsewhere.Answers);
        Assert.Equal(1, held.Elsewhere.Photos);
        Assert.Equal("2026 Phone Dump", held.Folder);
        Assert.Equal(0, held.Missing.Answers);
    }

    [Fact]
    public async Task TheFolderIsNamedOnlyWhenTheyAllShareOne()
    {
        // An instruction to go and move a folder cannot name two of them, so it
        // names none and the sentence says so instead.
        PhotoSource phone = Mum.Folder(@"C:\Users\Mum\Pictures");
        Person ana = Mum.Person("Ana");

        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\b.jpg", into: phone), Head),
            ana,
            AssignmentSource.Confirmed,
            Monday);

        Mum.Answer(
            Mum.Face(Mum.Photo(@"Scans\c.jpg", into: phone), Head),
            ana,
            AssignmentSource.Confirmed,
            Monday);

        HeldStanding held = await MergedAsync();

        Assert.Equal(2, held.Elsewhere.Photos);
        Assert.Empty(held.Folder);
    }

    /// <summary>
    /// Answers are counted as answers and photographs as photographs.
    /// </summary>
    /// <remarks>
    /// One picture can carry several answers of different kinds at once, and the
    /// two sentences need different units: the evening's work at risk is counted
    /// in answers, while a folder somebody has to go and move is counted in
    /// photographs.
    /// </remarks>
    [Fact]
    public async Task SeveralAnswersAboutOnePictureAreStillOnePhotograph()
    {
        PhotoSource phone = Mum.Folder(@"C:\Users\Mum\Pictures");
        Asset picture = Mum.Photo(@"2026 Phone Dump\b.jpg", into: phone);
        Person ana = Mum.Person("Ana");

        Mum.Answer(Mum.Face(picture, Head), ana, AssignmentSource.Confirmed, Monday);
        Mum.Answer(Mum.Face(picture, Elsewhere), ana, AssignmentSource.Confirmed, Monday);

        HeldStanding held = await MergedAsync();

        Assert.Equal(2, held.Elsewhere.Answers);
        Assert.Equal(1, held.Elsewhere.Photos);
    }

    /// <summary>
    /// The boundary that fails loudly if the matching is ever moved into SQL.
    /// </summary>
    /// <remarks>
    /// A held row's path has been through <see cref="AssetKey"/> and an asset
    /// row's has not. Windows treats <c>a/b.jpg</c> and <c>a\b.jpg</c> as one
    /// path and <c>IMG.JPG</c> and <c>img.jpg</c> as one file; SQLite's default
    /// collation treats neither pair as equal, so a join would file this
    /// photograph as one Dad does not have and the screen would ask him to go
    /// looking for a picture that is already on his disk.
    /// </remarks>
    [Fact]
    public async Task APathDifferingOnlyInCaseOrSeparatorIsTheSamePhotograph()
    {
        Mum.Answer(
            Mum.Face(Mum.Photo("2019/a.JPG"), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        Dad.Photo(@"2019\a.jpg");

        HeldStanding held = await MergedAsync();

        Assert.Equal(1, held.Faces.Answers);
        Assert.Equal(0, held.Missing.Answers);
    }

    [Fact]
    public async Task NothingWaitingIsNothingInEveryGroup()
    {
        HeldStanding held = await Dad.Decisions.HeldStandingAsync();

        Assert.Equal(0, held.Answers);
        Assert.Equal(0, held.Faces.Answers);
        Assert.Equal(0, held.Missing.Answers);
        Assert.Equal(0, held.Elsewhere.Answers);
        Assert.Empty(held.Folder);
    }

    /// <summary>
    /// The three groups account for every row, whatever else changes.
    /// </summary>
    /// <remarks>
    /// The guard against a fourth kind of waiting answer being added later and
    /// falling off the screen silently, which is what the single unfiltered
    /// count did to all three of these.
    /// </remarks>
    [Fact]
    public async Task TheThreeGroupsAccountForEveryHeldRow()
    {
        PhotoSource phone = Mum.Folder(@"C:\Users\Mum\Pictures");
        Person ana = Mum.Person("Ana");

        // One of each: a photograph Dad has, one he does not, and one in a folder
        // he has never heard of.
        Mum.Answer(Mum.Face(Mum.Photo(@"2019\a.jpg"), Head), ana, AssignmentSource.Confirmed, Monday);
        Mum.Answer(Mum.Face(Mum.Photo(@"2019\b.jpg"), Head), ana, AssignmentSource.Confirmed, Monday);
        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\c.jpg", into: phone), Head),
            ana,
            AssignmentSource.Confirmed,
            Monday);

        Dad.Photo(@"2019\a.jpg");

        HeldStanding held = await MergedAsync();

        Assert.Equal(1, held.Faces.Answers);
        Assert.Equal(1, held.Missing.Answers);
        Assert.Equal(1, held.Elsewhere.Answers);
        Assert.Equal(Dad.Db.HeldDecisions.Count(), held.Answers);
    }

    /// <summary>
    /// A folder that turns out to be one this library has stops being elsewhere -
    /// whichever way the two identities happen to sort.
    /// </summary>
    /// <remarks>
    /// <strong>This used to work half the time, and which half was decided by a
    /// coin toss.</strong> A pairing settles both folders on the lower of the two
    /// identities, and the rename it emitted covered only ids this library
    /// already owned. So when the other machine's id was the lower one, this
    /// library adopted it and its parked answers came with it; when its own id
    /// was lower it kept it, no rename was emitted for the other id at all, and
    /// every answer parked under that id was stranded - naming a folder that was
    /// now the same folder, counted under "elsewhere" for ever, released by no
    /// scan and no later pairing.
    ///
    /// <para>Run as a theory over both orderings, because a `Guid.NewGuid()` on
    /// each side made this a test that passed about half the time and was read as
    /// flaky rather than as the report it was.</para>
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerWhoseFolderIsLaterPairedStopsBeingElsewhere(bool theirsSortsLower)
    {
        PhotoSource phone = Mum.Folder(
            @"C:\Users\Mum\Pictures",
            theirsSortsLower ? Below(Dad.SharedSourceId) : Above(Dad.SharedSourceId));

        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\b.jpg", into: phone), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        HeldStanding before = await MergedAsync();
        Assert.Equal(1, before.Elsewhere.Answers);

        // Dad says that folder and his own are one, which is the one step in this
        // feature nobody can work out for him.
        await Dad.Pairing.HandleAsync(Dad.SharedSourceId, phone.SharedId);

        HeldStanding after = await Dad.Decisions.HeldStandingAsync();

        Assert.Equal(0, after.Elsewhere.Answers);
        Assert.Equal(1, after.Missing.Answers);

        // And it stays that way through the next scan. The row's folder was
        // renamed and the answer inside it was not, so the sweep read the old
        // folder back out of it and parked the answer there a second time.
        await Dad.Waiting.HandleAsync();

        HeldStanding swept = await Dad.Decisions.HeldStandingAsync();

        Assert.Equal(0, swept.Elsewhere.Answers);
        Assert.Single(Dad.Db.HeldDecisions);
    }

    /// <summary>
    /// An answer parked before its folder was paired lands when the photograph
    /// arrives, whichever way the two identities sort.
    /// </summary>
    /// <remarks>
    /// What pairing is for. Renaming only the row's folder column left the
    /// answer inside naming the folder by its old identity - the one the sweep
    /// reads - so it looked for a photograph in a folder this library no longer
    /// calls that, and waited for ever.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerParkedBeforeItsFolderWasPairedLandsWhenThePhotographArrives(
        bool theirsSortsLower)
    {
        PhotoSource phone = Mum.Folder(
            @"C:\Users\Mum\Pictures",
            theirsSortsLower ? Below(Dad.SharedSourceId) : Above(Dad.SharedSourceId));

        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\b.jpg", into: phone), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        await MergedAsync();
        await Dad.Pairing.HandleAsync(Dad.SharedSourceId, phone.SharedId);

        Dad.Face(Dad.Photo(@"2026 Phone Dump\b.jpg"), Head);

        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Equal(1, swept.Applied);
        Assert.Single(Dad.Db.FaceAssignments);
        Assert.Empty(Dad.Db.HeldDecisions);
    }

    /// <summary>
    /// A row this version cannot read still moves with its folder, as it was.
    /// </summary>
    /// <remarks>
    /// Rewriting the answer inside means reading it first, and a row written by
    /// a newer release may not read. It keeps what it says rather than being
    /// rewritten into something else or dropped - the bargain every reader of
    /// these rows strikes - and the folder it is filed under moves regardless.
    /// </remarks>
    [Fact]
    public async Task ARowThatCannotBeReadStillMovesWithItsFolder()
    {
        PhotoSource phone = Mum.Folder(@"C:\Users\Mum\Pictures", Above(Dad.SharedSourceId));
        const string unreadable = "{ \"from\": \"a newer release\" }";

        Dad.Db.HeldDecisions.Add(new HeldDecision
        {
            SharedSourceId = phone.SharedId,
            RelativePath = @"2026 Phone Dump\b.jpg",
            Kind = HeldDecisionKind.Turn,
            Part = string.Empty,
            Payload = unreadable,
            FromMachine = Mum.MachineId,
            DecidedUtc = Monday,
        });
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        await Dad.Pairing.HandleAsync(Dad.SharedSourceId, phone.SharedId);

        HeldDecision moved = Dad.Db.HeldDecisions.Single();
        Assert.Equal(Dad.SharedSourceId, moved.SharedSourceId);
        Assert.Equal(unreadable, moved.Payload);
    }

    /// <summary>
    /// Two folders holding the same path can both have an answer parked, and
    /// pairing them must not fall over.
    /// </summary>
    /// <remarks>
    /// Two rows about <c>IMG_0001.jpg</c>, one under each folder's identity, are
    /// the ordinary result of a phone dump sitting beside the folder its
    /// photographs were copied into. Pairing settles both on one identity, and
    /// the two rows become one - which the unique index over
    /// (source, path, kind, part) will not let them do by themselves. The
    /// duplicate is dropped rather than allowed to abort the pairing: both rows
    /// say the same thing about the same photograph.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PairingTwoFoldersThatHoldTheSamePathKeepsOneAnswer(bool theirsSortsLower)
    {
        PhotoSource phone = Mum.Folder(
            @"C:\Users\Mum\Pictures",
            theirsSortsLower ? Below(Dad.SharedSourceId) : Above(Dad.SharedSourceId));

        Person ana = Mum.Person("Ana");

        // The same photograph, by name, in both of Mum's folders - and Dad has
        // neither, so both answers park.
        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\IMG_0001.jpg"), Head),
            ana,
            AssignmentSource.Confirmed,
            Monday);

        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\IMG_0001.jpg", into: phone), Head),
            ana,
            AssignmentSource.Confirmed,
            Monday);

        HeldStanding before = await MergedAsync();
        Assert.Equal(2, before.Answers);

        await Dad.Pairing.HandleAsync(Dad.SharedSourceId, phone.SharedId);

        HeldStanding after = await Dad.Decisions.HeldStandingAsync();

        Assert.Equal(0, after.Elsewhere.Answers);
        Assert.Equal(1, after.Missing.Answers);
        Assert.Equal(1, Dad.Db.HeldDecisions.Count());
    }

    /// <summary>
    /// A fresh identity that sorts below the one given, so a pairing adopts it.
    /// </summary>
    /// <remarks>
    /// Generated rather than pinned to a constant: <see cref="Guid"/> compares
    /// its first field as a signed integer, so the obvious bookends
    /// (<c>000...1</c> and <c>fff...f</c>) do not sort the way they read, and a
    /// test pinned to one of them is a test that quietly checks the wrong thing.
    /// </remarks>
    private static Guid Below(Guid other) => Beside(other, -1);

    /// <summary>A fresh identity that sorts above the one given.</summary>
    private static Guid Above(Guid other) => Beside(other, 1);

    private static Guid Beside(Guid other, int sign)
    {
        for (int tries = 0; tries < 1000; tries++)
        {
            Guid candidate = Guid.NewGuid();

            if (Math.Sign(candidate.CompareTo(other)) == sign)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("no Guid on that side of the one given");
    }

    private async Task<HeldStanding> MergedAsync()
    {
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        return await Dad.Decisions.HeldStandingAsync();
    }

    public void Dispose() => _house.Dispose();
}
