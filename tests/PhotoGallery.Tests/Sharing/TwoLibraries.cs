using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Sharing;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Two libraries, two working folders, and a folder between them - the smallest
/// arrangement in which anything about sharing is true.
/// </summary>
/// <remarks>
/// Every rule in the sharing design is about two machines disagreeing, and a
/// single working folder can express none of them. Written before the merge
/// rather than after the third merge bug, which is the mistake this exists to
/// avoid: without it each test builds its own pair of contexts, they drift, and
/// the interesting half of the feature is asserted against whichever library the
/// last test happened to leave behind.
///
/// <para>The two are deliberately asymmetric in nothing except identity. Same
/// migrations, same shape, same paired source - so any difference a test sees is
/// one the test put there.</para>
/// </remarks>
internal sealed class TwoLibraries : IDisposable
{
    private readonly string _root;

    public TwoLibraries()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-sharing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        // One shared id across both, which is what pairing produces and what
        // every key is scoped by. A test that wants two machines with nothing in
        // common repoints one of them.
        Guid shared = Guid.NewGuid();

        SharedFolder = Path.Combine(_root, "shared");
        Directory.CreateDirectory(SharedFolder);

        Mum = new Library(Path.Combine(_root, "mum"), "Mum's laptop", shared);
        Dad = new Library(Path.Combine(_root, "dad"), "Dad's laptop", shared);
    }

    /// <summary>The library the answers are usually given on.</summary>
    public Library Mum { get; }

    /// <summary>The library they usually have to reach.</summary>
    public Library Dad { get; }

    /// <summary>The folder both machines write their answers into.</summary>
    public string SharedFolder { get; }

    /// <summary>A third machine, for the rules that only three can show.</summary>
    public Library Add(string name)
    {
        var third = new Library(
            Path.Combine(_root, name.Replace(' ', '-')), name, Mum.SharedSourceId);

        third.SharesThrough(SharedFolder);
        _others.Add(third);
        return third;
    }

    /// <summary>Points every machine at the folder between them.</summary>
    public TwoLibraries Sharing()
    {
        Mum.SharesThrough(SharedFolder);
        Dad.SharesThrough(SharedFolder);
        return this;
    }

    private readonly List<Library> _others = [];

    public void Dispose()
    {
        Mum.Dispose();
        Dad.Dispose();

        foreach (Library other in _others)
        {
            other.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle the runtime has not finished with yet. The folder
            // is under the temp root and costs nothing to leave behind; failing
            // a passing test over it would cost a great deal.
        }
    }
}

/// <summary>One machine's library: its own working folder, index and identity.</summary>
internal sealed class Library : IDisposable
{
    private int _nextPhoto;

    public Library(string root, string name, Guid sharedSourceId)
    {
        Root = root;
        Directory.CreateDirectory(root);

        Db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "index.db")}")
                .Options);
        Db.Database.Migrate();

        MachineId = Guid.NewGuid();
        Name = name;
        SharedSourceId = sharedSourceId;

        Db.LibrarySettings.Add(new LibrarySettings
        {
            Id = 1,
            MachineId = MachineId,
            MachineName = name,
        });

        Db.PhotoSources.Add(new PhotoSource
        {
            Id = 1,
            Path = root,
            SharedId = sharedSourceId,
            AddedUtc = DateTime.UtcNow,
        });

        Db.SaveChanges();
    }

    public GalleryDbContext Db { get; }

    /// <summary>The real implementations, so a test exercises what ships.</summary>
    public SqliteLibraryIndex Index => field ??= new SqliteLibraryIndex(Db);

    public SqliteDecisionReader Decisions => field ??= new SqliteDecisionReader(Db);

    public SqliteDecisionRepository Writing => field ??= new SqliteDecisionRepository(Db);

    public SharedFolderExchange Exchange => field ??= new SharedFolderExchange(Index);

    public PublishDecisionsHandler Publishing =>
        field ??= new PublishDecisionsHandler(Index, Decisions, Exchange);

    public MergedTurns Turning => field ??= new MergedTurns(
        Decisions, Writing, Renditions, new SqliteFaceRepository(Db));

    public MergeDecisionsHandler Merging =>
        field ??= new MergeDecisionsHandler(Index, Decisions, Writing, Exchange, Turning);

    /// <summary>The scan phase that applies answers which were waiting.</summary>
    public ApplyHeldDecisionsHandler Waiting =>
        field ??= new ApplyHeldDecisionsHandler(Index, Decisions, Writing, Turning);

    /// <summary>What the Sharing screen opens showing.</summary>
    public GetSharingHandler Overview => field ??= new GetSharingHandler(Index, Decisions, Exchange);

    /// <summary>Its one button: take everybody's answers, then give yours back.</summary>
    public ShareNowHandler Sharing => field ??= new ShareNowHandler(Merging, Publishing);

    /// <summary>Confirming that two folders, reached two ways, are one.</summary>
    public ConfirmPairingHandler Pairing =>
        field ??= new ConfirmPairingHandler(Index, Decisions, Writing);

    /// <summary>Stands in for the cached pictures, which these tests have none of.</summary>
    public StubRenditionTurner Renditions { get; } = new();

    public string Root { get; }

    public Guid MachineId { get; }

    public string Name { get; }

    /// <summary>What this machine's source is called on both machines.</summary>
    public Guid SharedSourceId { get; private set; }

    /// <summary>Nominates the folder this machine shares answers through.</summary>
    public void SharesThrough(string folder)
    {
        LibrarySettings settings = Db.LibrarySettings.Single();
        settings.SharedFolder = folder;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Repoints the source at a different shared id, which is what two machines
    /// that were never paired look like.
    /// </summary>
    public void Unpair()
    {
        SharedSourceId = Guid.NewGuid();
        Db.PhotoSources.Single().SharedId = SharedSourceId;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Reaches the same photographs by a different route, and under an identity
    /// nobody has matched yet - which is exactly the state two machines are in
    /// before anybody pairs them.
    /// </summary>
    public void Reaches(string root)
    {
        Unpair();

        PhotoSource source = Db.PhotoSources.Single();
        source.Path = root;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>What this library's source is called after a rename.</summary>
    public Guid CurrentSharedId => Db.PhotoSources.AsNoTracking().Single().SharedId;

    /// <summary>
    /// Runs a real scan over a folder with nothing in it, so every indexed
    /// photograph is one the scan finds gone.
    /// </summary>
    /// <remarks>
    /// Through the scanner rather than by deleting rows, because the claim under
    /// test is about what a scan does when a file is not where the source says
    /// it is - and a test that removed the rows itself would be asserting the
    /// parking that it had just arranged.
    /// </remarks>
    public async Task ScanFindsNothingAsync()
    {
        string empty = Path.Combine(Root, "source");
        Directory.CreateDirectory(empty);

        PhotoSource source = Db.PhotoSources.Single();
        source.Path = empty;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        var working = new WorkingFolder(Path.Combine(Root, "working"));
        working.EnsureCreated();

        var scan = new ScanPhotoSourceHandler(
            Index,
            new SqliteAssetRepository(Db),
            new MediaFileWalker(working),
            new FileSystemThumbnailStore(working),
            Decisions,
            Writing);

        await scan.HandleAsync(source.Id);
        Db.ChangeTracker.Clear();
    }

    /// <summary>Indexes a photograph, as a crawl would.</summary>
    public Asset Photo(string relativePath, DateTime? takenUtc = null)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024 + _nextPhoto++,
            ModifiedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IndexedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            TakenUtc = takenUtc,
            Width = 1000,
            Height = 800,
        };

        Db.Assets.Add(asset);
        Db.SaveChanges();
        return asset;
    }

    /// <summary>Records a detected face, as the face pass would.</summary>
    public Face Face(Asset asset, FaceBounds bounds, double degrees = 0d)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var face = new Face
        {
            AssetId = asset.Id,
            Bounds = bounds,
            DetectScore = 0.99f,
            Embedding = TestEmbeddings.At(degrees),
        };

        Db.Faces.Add(face);
        Db.SaveChanges();
        return face;
    }

    /// <summary>Creates somebody, as naming a face would.</summary>
    public Person Person(string displayName, Guid? publicId = null)
    {
        var person = new Person
        {
            PublicId = publicId ?? Guid.NewGuid(),
            DisplayName = displayName,
        };

        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    /// <summary>Answers a face, with the moment the answer was given.</summary>
    public FaceAssignment Answer(
        Face face,
        Person person,
        AssignmentSource source,
        DateTime decidedUtc)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(person);

        var assignment = new FaceAssignment
        {
            FaceId = face.Id,
            PersonId = person.Id,
            Source = source,
            DecidedUtc = decidedUtc,
        };

        Db.FaceAssignments.Add(assignment);
        Db.SaveChanges();
        return assignment;
    }

    /// <summary>An album somebody made, as opposed to one the app proposed.</summary>
    public Collection Album(string name, DateTime namedUtc, Guid? publicId = null)
    {
        var album = new Collection
        {
            PublicId = publicId ?? Guid.NewGuid(),
            Name = name,
            StartUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Kind = CollectionKind.Period,
            Origin = CollectionOrigin.Made,
            NamedUtc = namedUtc,
            BuiltUtc = namedUtc,
        };

        Db.Collections.Add(album);
        Db.SaveChanges();
        return album;
    }

    /// <summary>What this machine calls a photograph when telling another about it.</summary>
    public AssetKey KeyOf(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new AssetKey(SharedSourceId, asset.RelativePath);
    }

    public void Dispose() => Db.Dispose();
}
