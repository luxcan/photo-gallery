using PhotoGallery.Application.Ports;
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
    /// A folder that turns out to be one this library has stops being elsewhere.
    /// </summary>
    /// <remarks>
    /// <strong>Only in one direction, and that is a bug this test does not
    /// fix.</strong> A pairing adopts the lower of the two identities
    /// (<c>SourcePairing.Adopt</c>), and rekeys held rows only where the id being
    /// renamed is one of <em>this</em> library's. So when the other machine's id
    /// is the lower one - as it is pinned to be here - this library adopts it and
    /// its held answers line up. When this library's own id is lower it keeps it,
    /// and answers parked under the other id are about a folder that is now the
    /// same folder and are still keyed as though it were not: they stay elsewhere
    /// for ever, and no scan and no pairing will release them.
    ///
    /// <para>The split reports that faithfully - the row really does name a
    /// folder this library does not have - so the screen is honest either way.
    /// The fix belongs in the pairing, not here.</para>
    /// </remarks>
    [Fact]
    public async Task AnAnswerWhoseFolderIsLaterPairedStopsBeingElsewhere()
    {
        // The lower of the two identities is the one a pairing adopts, so this
        // one is pinned rather than left to a fresh Guid to decide.
        PhotoSource phone = Mum.Folder(
            @"C:\Users\Mum\Pictures", new Guid("00000000-0000-0000-0000-000000000001"));

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
    }

    private async Task<HeldStanding> MergedAsync()
    {
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        return await Dad.Decisions.HeldStandingAsync();
    }

    public void Dispose() => _house.Dispose();
}
