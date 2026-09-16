using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

public sealed class QueryGalleryHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteGalleryReader _reader;
    private int _nextId = 1;

    public QueryGalleryHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-gallery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = @"C:\one" });
        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 2, Path = @"C:\two" });
        _db.SaveChanges();

        _reader = new SqliteGalleryReader(_db);
    }

    private QueryGalleryHandler NewHandler() => new(_reader);

    [Fact]
    public async Task Query_WithNoLimit_ReturnsEverything()
    {
        // The grid asks for the whole library and virtualises rows, so a default
        // that quietly truncated would silently hide most of it.
        for (int i = 0; i < 250; i++)
        {
            Add($@"2020\{i}.jpg");
        }

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Equal(250, page.TotalCount);
        Assert.Equal(250, page.Items.Count);
    }

    [Fact]
    public async Task Query_OrdersByTheSameDateItReports()
    {
        // The order is decided in SQL and the date is reported in memory, from
        // two copies of one rule. If they ever disagree a picture sits in one
        // place and says it belongs in another, so this compares them directly.
        Add(@"a\copied.jpg",
            modified: new DateTime(2014, 3, 11), created: new DateTime(2026, 4, 26));
        Add(@"a\intact.jpg",
            modified: new DateTime(2021, 9, 9), created: new DateTime(2019, 2, 2));
        Add(@"a\dated.jpg",
            modified: new DateTime(2024, 1, 1), taken: new DateTime(2013, 7, 7));

        GalleryPage page = await NewHandler().HandleAsync(
            new GalleryQuery { SortOrder = GallerySortOrder.OldestFirst });

        Assert.Equal(
            ["dated.jpg", "copied.jpg", "intact.jpg"],
            page.Items.Select(item => item.FileName));

        // ...and the dates the rows carry are in that same order.
        Assert.Equal(
            page.Items.Select(item => item.SortedOn).OrderBy(date => date),
            page.Items.Select(item => item.SortedOn));
    }

    [Fact]
    public async Task Query_FallsBackToTheEarlierOfTheFilesTwoDates()
    {
        // A file whose creation date survived the trip is dated by it; one whose
        // creation date was stamped by a bulk copy is dated by its modified
        // date. Measured on a real library, the earlier of the two lands on the
        // true capture day 7,527 times against 7,309 for modified alone.
        Add(@"a\intact.jpg",
            modified: new DateTime(2021, 9, 9), created: new DateTime(2019, 2, 2));
        Add(@"a\copied.jpg",
            modified: new DateTime(2014, 3, 11), created: new DateTime(2026, 4, 26));

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Equal(
            new DateTime(2019, 2, 2),
            page.Items.Single(item => item.FileName == "intact.jpg").SortedOn);
        Assert.Equal(
            new DateTime(2014, 3, 11),
            page.Items.Single(item => item.FileName == "copied.jpg").SortedOn);
    }

    [Fact]
    public async Task Query_ReturnsNewestFirstByTakenDate()
    {
        Add(@"a\old.jpg", modified: new DateTime(2020, 1, 1));
        Add(@"a\newest.jpg", modified: new DateTime(2019, 1, 1), taken: new DateTime(2024, 5, 5));
        Add(@"a\middle.jpg", modified: new DateTime(2022, 1, 1));

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Equal(
            ["newest.jpg", "middle.jpg", "old.jpg"],
            page.Items.Select(i => i.FileName));
    }

    [Fact]
    public async Task Query_ReturnsOldestFirstWhenAsked()
    {
        Add(@"a\old.jpg", modified: new DateTime(2020, 1, 1));
        Add(@"a\newest.jpg", modified: new DateTime(2019, 1, 1), taken: new DateTime(2024, 5, 5));
        Add(@"a\middle.jpg", modified: new DateTime(2022, 1, 1));

        GalleryPage page = await NewHandler().HandleAsync(
            new GalleryQuery(SortOrder: GallerySortOrder.OldestFirst));

        Assert.Equal(
            ["old.jpg", "middle.jpg", "newest.jpg"],
            page.Items.Select(i => i.FileName));
    }

    [Fact]
    public async Task Query_ReversesTheTieBreakAlongWithTheOrder()
    {
        // 1,964 photos in the real library share an exact timestamp with another.
        // If the Id tie-break kept its direction while the dates flipped, those
        // groups would run backwards against everything around them - and the
        // one-photo view walks this order, so it would jump.
        Add(@"a\first.jpg", modified: new DateTime(2021, 1, 1));
        Add(@"a\second.jpg", modified: new DateTime(2021, 1, 1));
        Add(@"a\third.jpg", modified: new DateTime(2021, 1, 1));

        GalleryPage newest = await NewHandler().HandleAsync(new GalleryQuery());
        GalleryPage oldest = await NewHandler().HandleAsync(
            new GalleryQuery(SortOrder: GallerySortOrder.OldestFirst));

        Assert.Equal(
            newest.Items.Select(i => i.FileName).Reverse(),
            oldest.Items.Select(i => i.FileName));
    }

    [Fact]
    public async Task Query_FallsBackToTheFileDateWhereThereIsNoCaptureDate()
    {
        // Creation stamped later than modification, which is what a copy does,
        // so the modified date is the earlier of the two and the one used.
        Add(@"a\one.jpg",
            modified: new DateTime(2021, 6, 1), created: new DateTime(2026, 4, 26));

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Null(page.Items[0].TakenUtc);
        Assert.Equal(new DateTime(2021, 6, 1), page.Items[0].SortedOn);
    }

    [Fact]
    public async Task Query_BreaksTiesByIdSoTheOrderIsStable()
    {
        var shared = new DateTime(2020, 3, 3, 12, 0, 0);
        Add(@"a\first.jpg", modified: shared);
        Add(@"a\second.jpg", modified: shared);
        Add(@"a\third.jpg", modified: shared);

        GalleryPage one = await NewHandler().HandleAsync(new GalleryQuery());
        GalleryPage two = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Equal(["third.jpg", "second.jpg", "first.jpg"], one.Items.Select(i => i.FileName));
        Assert.Equal(one.Items.Select(i => i.Id), two.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Query_IncludesVideosThatHaveAPoster()
    {
        Add(@"a\photo.jpg");
        Add(@"a\clip.mov", kind: AssetKind.Video, thumbnailName: "abc.jpg");

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, i => i.Kind == AssetKind.Video);
    }

    [Fact]
    public async Task Query_LeavesOutAVideoWithNoPosterYet()
    {
        // Not the same rule as a photograph, on purpose. An unprepared
        // photograph shows a placeholder for the minutes until the pass that
        // follows the scan reaches it; a video waits on a pass somebody has to
        // choose to start, and 4,743 grey cells interleaved by date is what kept
        // videos out of the grid in the first place.
        Add(@"a\photo.jpg");
        Add(@"a\clip.mov", kind: AssetKind.Video);

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Equal(1, page.TotalCount);
        Assert.DoesNotContain(page.Items, i => i.Kind == AssetKind.Video);
    }

    [Fact]
    public async Task Query_CarriesHowLongAClipRuns()
    {
        Add(
            @"a\clip.mov",
            kind: AssetKind.Video,
            thumbnailName: "abc.jpg",
            duration: TimeSpan.FromSeconds(95));

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Equal(TimeSpan.FromSeconds(95), Assert.Single(page.Items).Duration);
    }

    [Fact]
    public async Task Query_SaysNothingAboutTheLengthOfAClipThatNeverGaveOne()
    {
        // What the shell extractor gives back for every video it opens. The
        // badge shows the glyph alone rather than inventing a figure.
        Add(@"a\clip.mov", kind: AssetKind.Video, thumbnailName: "abc.jpg");

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Null(Assert.Single(page.Items).Duration);
    }

    [Fact]
    public async Task Query_ExcludesVideosWhenAsked()
    {
        Add(@"a\photo.jpg");
        Add(@"a\clip.mov", kind: AssetKind.Video, thumbnailName: "abc.jpg");

        GalleryPage page = await NewHandler().HandleAsync(
            new GalleryQuery(IncludeVideos: false));

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Query_FolderFilterExcludesAPrefixSibling()
    {
        // Eight pairs of real folders collide this way, including 20220201
        // beside "20220201 - CNY".
        Add(@"20220201\in.jpg");
        Add(@"20220201 - CNY\out.jpg");

        GalleryPage page = await NewHandler().HandleAsync(
            new GalleryQuery(PhotoSourceId: 1, FolderPath: "20220201"));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("in.jpg", page.Items[0].FileName);
    }

    [Fact]
    public async Task Query_FolderFilterIncludesNestedFolders()
    {
        Add(@"trip\one.jpg");
        Add(@"trip\signs\two.jpg");

        GalleryPage page = await NewHandler().HandleAsync(
            new GalleryQuery(PhotoSourceId: 1, FolderPath: "trip"));

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Query_FolderFilterTreatsUnderscoreLiterally()
    {
        // 46 of 219 folders here contain an underscore, which is a single
        // character wildcard to LIKE.
        Add(@"2015_Ana\in.jpg");
        Add(@"2015XAna\out.jpg");

        GalleryPage page = await NewHandler().HandleAsync(
            new GalleryQuery(PhotoSourceId: 1, FolderPath: "2015_Ana"));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("in.jpg", page.Items[0].FileName);
    }

    [Fact]
    public async Task Query_FolderFilterIsScopedToItsSource()
    {
        Add(@"shared\mine.jpg", source: 1);
        Add(@"shared\theirs.jpg", source: 2);

        GalleryPage page = await NewHandler().HandleAsync(
            new GalleryQuery(PhotoSourceId: 1, FolderPath: "shared"));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("mine.jpg", page.Items[0].FileName);
    }

    [Fact]
    public async Task Query_FolderWithoutItsSourceIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => NewHandler().HandleAsync(new GalleryQuery(FolderPath: "somewhere")));
    }

    [Fact]
    public async Task Query_TotalCountIgnoresPaging()
    {
        for (int i = 0; i < 10; i++)
        {
            Add($@"a\{i}.jpg");
        }

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery(Take: 3));

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(10, page.TotalCount);
    }

    [Fact]
    public async Task Query_OnAnEmptyLibraryReturnsNothingWithoutThrowing()
    {
        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Query_CarriesTheFolderSoTheViewCanNameIt()
    {
        Add(@"20200214_Ana Lim Born\one.jpg");

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery());

        Assert.Equal("20200214_Ana Lim Born", page.Items[0].FolderPath);
        Assert.Equal("one.jpg", page.Items[0].FileName);
    }

    [Fact]
    public async Task Query_ForOnePersonReturnsTheirPicturesFromEveryFolder()
    {
        // The acceptance the whole feature exists for: a name gives back photos
        // from folders whose names mention nobody.
        Add(@"20200214_Ana Lim Born\one.jpg");
        Add(@"20230211 - Chingay\two.jpg");
        Add(@"20230211 - Chingay\someone-else.jpg");

        int person = AddPerson("Ana Lim");
        Claim(assetId: 1, person, AssignmentSource.Confirmed);
        Claim(assetId: 2, person, AssignmentSource.Confirmed);
        Claim(assetId: 3, person, AssignmentSource.Proposed);

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery(PersonId: person));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(
            ["two.jpg", "one.jpg"],
            page.Items.Select(item => item.FileName));
    }

    [Fact]
    public async Task Query_ForOnePersonIgnoresFacesOnlyProposedAsTheirs()
    {
        // A proposal is a question the user has not answered. Counting it would
        // answer it for them and make the review pointless.
        Add(@"a\maybe.jpg");
        int person = AddPerson("Ana Reyes");
        Claim(assetId: 1, person, AssignmentSource.Proposed);

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery(PersonId: person));

        Assert.Equal(0, page.TotalCount);
    }

    private int AddPerson(string name)
    {
        var person = new Person { DisplayName = name };
        _db.Set<Person>().Add(person);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return person.Id;
    }

    private void Claim(int assetId, int personId, AssignmentSource source)
    {
        float[] values = new float[FaceEmbedding.Dimensions];
        values[0] = 1f;

        var face = new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(0, 0, 60, 60),
            DetectScore = 0.9f,
            Embedding = new FaceEmbedding(values),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();

        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = face.Id, PersonId = personId, Source = source,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    /// <summary>
    /// The wall narrowed to what is in no album at all.
    /// </summary>
    /// <remarks>
    /// A suggested album counts as an album. Counting only albums somebody kept
    /// matched 17,876 of the first real library's 20,269 photographs, because
    /// almost every album in it is a suggestion nobody has answered - and a
    /// filter that matches seven-eighths of the wall is not a filter.
    /// </remarks>
    [Fact]
    public async Task Query_OutsideAlbums_LeavesOutAnythingInOne()
    {
        Add("filed.jpg");
        Add("loose.jpg");
        Add("suggested.jpg");

        InAlbum(asset: 1, album: Album("Bali", AlbumOrigin.Made));
        InAlbum(asset: 3, album: Album("A week in March", AlbumOrigin.Proposed));

        GalleryPage page = await NewHandler()
            .HandleAsync(new GalleryQuery(OnlyOutsideAlbums: true));

        Assert.Equal(["loose.jpg"], page.Items.Select(item => item.FileName));
    }

    [Theory]
    [InlineData(AssetKind.Photo, "a.jpg")]
    [InlineData(AssetKind.Video, "b.mp4")]
    public async Task Query_ByKind_ShowsOnlyThatKind(AssetKind kind, string expected)
    {
        Add("a.jpg");
        Add("b.mp4", kind: AssetKind.Video, thumbnailName: "b.png");

        GalleryPage page = await NewHandler().HandleAsync(new GalleryQuery(Kind: kind));

        Assert.Equal([expected], page.Items.Select(item => item.FileName));
    }

    /// <summary>
    /// The two narrow together, which is the pair that makes the row worth
    /// having: 5,774 of the first real library's 6,295 videos are in no album.
    /// </summary>
    [Fact]
    public async Task Query_VideosOutsideAlbums_NarrowsOnBothAtOnce()
    {
        Add("filed.mp4", kind: AssetKind.Video, thumbnailName: "1.png");
        Add("loose.mp4", kind: AssetKind.Video, thumbnailName: "2.png");
        Add("loose.jpg");

        InAlbum(asset: 1, album: Album("Bali", AlbumOrigin.Made));

        GalleryPage page = await NewHandler().HandleAsync(
            new GalleryQuery(Kind: AssetKind.Video, OnlyOutsideAlbums: true));

        Assert.Equal(["loose.mp4"], page.Items.Select(item => item.FileName));
    }

    /// <summary>
    /// Nobody named. A proposed answer is a question, so it does not count as a
    /// name - the same rule the person filter keeps.
    /// </summary>
    [Fact]
    public async Task Query_WithoutNamedFaces_CountsOnlyConfirmedAnswers()
    {
        Add("named.jpg");
        Add("proposed.jpg");
        Add("nobody.jpg");

        Person ana = NewPerson("Ana");
        Named(asset: 1, ana, AssignmentSource.Confirmed);
        Named(asset: 2, ana, AssignmentSource.Proposed);

        GalleryPage page = await NewHandler()
            .HandleAsync(new GalleryQuery(OnlyWithoutNamedFaces: true));

        Assert.Equal(
            ["nobody.jpg", "proposed.jpg"],
            page.Items.Select(item => item.FileName).Order());
    }

    private int Album(string name, AlbumOrigin origin)
    {
        var album = new Album
        {
            Name = name,
            Origin = origin,
            Kind = AlbumKind.Period,
            StartUtc = new DateTime(2020, 1, 1),
            EndUtc = new DateTime(2020, 1, 2),
            BuiltUtc = new DateTime(2020, 1, 1),
        };

        _db.Albums.Add(album);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return album.Id;
    }

    private void InAlbum(int asset, int album)
    {
        _db.AlbumMembers.Add(new AlbumMember
        {
            AssetId = asset,
            AlbumId = album,
            AddedUtc = new DateTime(2020, 1, 1),
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private Person NewPerson(string name)
    {
        var person = new Person { PublicId = Guid.NewGuid(), DisplayName = name };
        _db.People.Add(person);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return person;
    }

    private void Named(int asset, Person person, AssignmentSource source)
    {
        var face = new Face
        {
            AssetId = asset,
            Bounds = new FaceBounds(1, 1, 10, 10),
            DetectScore = 0.9f,
            Embedding = TestEmbeddings.At(0),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();

        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = face.Id,
            PersonId = person.Id,
            Source = source,
            DecidedUtc = new DateTime(2020, 1, 1),
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private void Add(
        string relativePath,
        int source = 1,
        AssetKind kind = AssetKind.Photo,
        DateTime? modified = null,
        DateTime? taken = null,
        DateTime? created = null,
        string? thumbnailName = null,
        TimeSpan? duration = null)
    {
        _db.Assets.Add(new Asset
        {
            Id = _nextId++,
            PhotoSourceId = source,
            RelativePath = relativePath,
            Length = 1,
            ModifiedUtc = modified ?? new DateTime(2020, 1, 1),
            CreatedUtc = created ?? new DateTime(2020, 1, 1),
            IndexedUtc = new DateTime(2020, 1, 1),
            TakenUtc = taken,
            Kind = kind,
            ThumbnailName = thumbnailName,
            Duration = duration,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
