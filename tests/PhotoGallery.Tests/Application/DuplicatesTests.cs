using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Finding duplicates, setting them aside, and putting them back.
/// </summary>
/// <remarks>
/// This is the second feature in the app that touches the user's own files, and
/// the only one that touches them in bulk. What it refuses to do carries more
/// weight than what it does.
/// </remarks>
public sealed class DuplicatesTests : IDisposable
{
    private static readonly DateTime s_when = new(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly string _library;
    private readonly GalleryDbContext _db;
    private readonly SqliteDuplicateRepository _repository;
    private readonly FileSystemQuarantine _quarantine;

    public DuplicatesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-dupes-{Guid.NewGuid():N}");
        _library = Path.Combine(_root, "library");
        Directory.CreateDirectory(_library);

        var workingFolder = new WorkingFolder(Path.Combine(_root, "working"));
        workingFolder.EnsureCreated();
        _quarantine = new FileSystemQuarantine(workingFolder);

        _db = new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
            .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _library });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _repository = new SqliteDuplicateRepository(_db);
    }

    [Fact]
    public async Task Find_GroupsFilesWithIdenticalBytesAndOpensNothing()
    {
        AddPhoto(@"20230201\a.jpg", hash: "aaaa");
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: "aaaa");
        AddPhoto(@"20230201\b.jpg", hash: "bbbb");

        DuplicateScan scan = await Find();

        Assert.Equal(1, scan.ExactSets);
        Assert.Equal(1, scan.ExactRedundant);
    }

    [Fact]
    public async Task Find_KeepsTheCopyInTheFolderThatSaysWhatItIs()
    {
        // A bare-date folder carries nothing beyond the date already on the file.
        // Measured on the real library, this reversed 110 of 230 decisions
        // against a naive shortest-path rule.
        AddPhoto(@"20230201\a.jpg", hash: "aaaa");
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: "aaaa");

        await Find();
        DuplicateSetView set = Assert.Single(await _repository.GetAsync(DuplicateKind.Exact));

        Assert.Equal(@"20230203 - Chingay\a.jpg", set.Keeper!.RelativePath);
    }

    [Fact]
    public async Task Find_KeepsTheBiggerPictureWhenTheCopiesAreOnlyAlike()
    {
        // A watermarked or re-saved copy is the one to lose. Both sit in the same
        // folder on the real library, so path order alone would have kept
        // whichever name happened to sort first.
        AddPhoto(@"20230201\original.jpg", hash: "aaaa", phash: 0b1111, width: 4000, height: 3000);
        AddPhoto(@"20230201\watermarked.jpg", hash: "bbbb", phash: 0b1111, width: 1600, height: 1200);

        await Find();
        DuplicateSetView set = Assert.Single(await _repository.GetAsync(DuplicateKind.Near));

        Assert.Equal(@"20230201\original.jpg", set.Keeper!.RelativePath);
    }

    [Fact]
    public async Task Find_DoesNotOfferTheSameFilesAsBothKinds()
    {
        // Byte-identical is already proved. Asking a second time in a weaker form
        // would double every set on the screen.
        AddPhoto(@"20230201\a.jpg", hash: "aaaa", phash: 0b1111);
        AddPhoto(@"20230201\b.jpg", hash: "aaaa", phash: 0b1111);

        DuplicateScan scan = await Find();

        Assert.Equal(1, scan.ExactSets);
        Assert.Equal(0, scan.NearSets);
    }

    [Fact]
    public async Task Find_LeavesCopiesAlreadySetAsideOutOfIt()
    {
        int kept = AddPhoto(@"20230203 - Chingay\a.jpg", hash: "aaaa");
        int gone = AddPhoto(@"20230201\a.jpg", hash: "aaaa");
        await _repository.SetQuarantinedAsync([gone], s_when);

        DuplicateScan scan = await Find();

        Assert.Equal(0, scan.ExactSets);
        Assert.Equal(1, scan.Weighed);
        Assert.Equal(kept, (await _db.Assets.AsNoTracking()
            .Where(a => a.QuarantinedUtc == null).SingleAsync()).Id);
    }

    [Fact]
    public async Task SetAside_MovesTheRedundantCopyAndLeavesTheKeeperAlone()
    {
        string keeper = WriteFile(@"20230203 - Chingay\a.jpg", "same");
        string redundant = WriteFile(@"20230201\a.jpg", "same");
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: Digest("same"));
        AddPhoto(@"20230201\a.jpg", hash: Digest("same"));
        await Find();

        int setId = (await _repository.GetAsync(DuplicateKind.Exact)).Single().Id;
        QuarantineResult result = await Quarantine().HandleAsync([setId]);

        Assert.Equal(1, result.Moved);
        Assert.Equal(0, result.Refused);
        Assert.True(File.Exists(keeper), "the keeper was moved");
        Assert.False(File.Exists(redundant), "the redundant copy is still in the library");
        Assert.True(File.Exists(_quarantine.PathFor(1, @"20230201\a.jpg")));
    }

    [Fact]
    public async Task SetAside_TakesTheCopyOutOfTheLibraryWithoutLosingItsRow()
    {
        // The row is what knows how to put the file back. Removing it - which is
        // what a scan does to a file that has gone - would make the quarantine a
        // one-way door.
        WriteFile(@"20230203 - Chingay\a.jpg", "same");
        WriteFile(@"20230201\a.jpg", "same");
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: Digest("same"));
        int redundant = AddPhoto(@"20230201\a.jpg", hash: Digest("same"));
        await Find();

        await Quarantine().HandleAsync([(await _repository.GetAsync(DuplicateKind.Exact)).Single().Id]);

        Asset row = await _db.Assets.AsNoTracking().SingleAsync(a => a.Id == redundant);
        Assert.NotNull(row.QuarantinedUtc);
        Assert.Equal(2, await _db.Assets.CountAsync());
    }

    [Fact]
    public async Task SetAside_LeavesTheSetUnfinishedWhenAFileWillNotMove()
    {
        // A set that looks dealt with while a copy nobody can find is still on
        // the share is the worst outcome available here.
        WriteFile(@"20230203 - Chingay\a.jpg", "same");
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: Digest("same"));
        AddPhoto(@"20230201\a.jpg", hash: Digest("same"));
        await Find();

        int setId = (await _repository.GetAsync(DuplicateKind.Exact)).Single().Id;

        // The redundant copy's file was never written, so it cannot be moved.
        QuarantineResult result = await Quarantine().HandleAsync([setId]);

        Assert.Equal(0, result.Moved);
        Assert.Equal(1, result.Refused);
        Assert.Equal(0, result.Sets);
        Assert.False((await _db.Set<DuplicateSet>().AsNoTracking().SingleAsync()).IsResolved);
    }

    [Fact]
    public async Task SetAside_KeepsTheOriginalWhenTheCopyIsNotTheFileItShouldBe()
    {
        // The library is on a network share. A transfer that corrupts bytes
        // without changing how many there are would pass a length check, and the
        // original is deleted on the strength of that answer - so the digest the
        // row already carries is what makes the delete safe.
        WriteFile(@"20230203 - Chingay\a.jpg", "same");

        // Four bytes either way, so only the digest can tell them apart.
        string redundant = WriteFile(@"20230201\a.jpg", "sane");
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: Digest("same"));
        AddPhoto(@"20230201\a.jpg", hash: Digest("same"));
        await Find();

        int setId = (await _repository.GetAsync(DuplicateKind.Exact)).Single().Id;
        QuarantineResult result = await Quarantine().HandleAsync([setId]);

        Assert.Equal(0, result.Moved);
        Assert.Equal(1, result.Refused);
        Assert.True(File.Exists(redundant), "the original was deleted for a copy that is wrong");
        Assert.False(File.Exists(_quarantine.PathFor(1, @"20230201\a.jpg")));
        Assert.False(File.Exists(_quarantine.PathFor(1, @"20230201\a.jpg") + ".partial"));
    }

    [Fact]
    public async Task PutBack_ReturnsEveryCopyToWhereItCameFrom()
    {
        WriteFile(@"20230203 - Chingay\a.jpg", "same");
        string redundant = WriteFile(@"20230201\a.jpg", "same");
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: Digest("same"));
        AddPhoto(@"20230201\a.jpg", hash: Digest("same"));
        await Find();
        await Quarantine().HandleAsync([(await _repository.GetAsync(DuplicateKind.Exact)).Single().Id]);
        Assert.False(File.Exists(redundant));

        RestoreResult result =
            await new RestoreQuarantineHandler(_repository, _quarantine).HandleAsync();

        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.Refused);
        Assert.True(File.Exists(redundant));
        Assert.Equal("same", await File.ReadAllTextAsync(redundant));
        Assert.False(File.Exists(_quarantine.PathFor(1, @"20230201\a.jpg")));
        Assert.All(
            await _db.Assets.AsNoTracking().ToListAsync(),
            asset => Assert.Null(asset.QuarantinedUtc));
    }

    [Fact]
    public async Task Find_DoesNotRevisitASetTheUserHasAlreadyDealtWith()
    {
        WriteFile(@"20230203 - Chingay\a.jpg", "same");
        WriteFile(@"20230201\a.jpg", "same");
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: Digest("same"));
        AddPhoto(@"20230201\a.jpg", hash: Digest("same"));
        await Find();
        await Quarantine().HandleAsync([(await _repository.GetAsync(DuplicateKind.Exact)).Single().Id]);

        DuplicateScan again = await Find();

        Assert.Equal(0, again.ExactSets);
        Assert.Empty(await _repository.GetAsync(DuplicateKind.Exact));
    }

    [Fact]
    public async Task SetKeeper_LetsTheUserOverrideWhichCopyStays()
    {
        // The app's choice is a rule about folder names and file sizes; the
        // user's is a decision about a photograph, made while looking at it.
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: "aaaa");
        int preferred = AddPhoto(@"20230201\a.jpg", hash: "aaaa");
        await Find();

        DuplicateSetView before = (await _repository.GetAsync(DuplicateKind.Exact)).Single();
        Assert.NotEqual(preferred, before.Keeper!.AssetId);

        await _repository.SetKeeperAsync(before.Id, preferred);

        DuplicateSetView after = (await _repository.GetAsync(DuplicateKind.Exact)).Single();
        Assert.Equal(preferred, after.Keeper!.AssetId);
        Assert.Single(after.Redundant);
    }

    [Fact]
    public async Task SetKeeper_MovesTheFileTheUserChoseToKeepOutOfHarmsWay()
    {
        // The point of overriding: whichever copy is kept must be the one still
        // in the library afterwards.
        string chosen = WriteFile(@"20230201\a.jpg", "same");
        string other = WriteFile(@"20230203 - Chingay\a.jpg", "same");
        int preferred = AddPhoto(@"20230201\a.jpg", hash: Digest("same"));
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: Digest("same"));
        await Find();

        int setId = (await _repository.GetAsync(DuplicateKind.Exact)).Single().Id;
        await _repository.SetKeeperAsync(setId, preferred);
        await Quarantine().HandleAsync([setId]);

        Assert.True(File.Exists(chosen), "the copy the user chose to keep was moved");
        Assert.False(File.Exists(other), "the other copy is still in the library");
    }

    [Fact]
    public async Task SetKeeper_IgnoresAnAssetThatIsNotInTheSet()
    {
        // Silently rewriting every role to Redundant would leave a set with no
        // keeper, which the screen would then refuse to show at all.
        AddPhoto(@"20230203 - Chingay\a.jpg", hash: "aaaa");
        AddPhoto(@"20230201\a.jpg", hash: "aaaa");
        int stranger = AddPhoto(@"20230201\b.jpg", hash: "bbbb");
        await Find();

        DuplicateSetView set = (await _repository.GetAsync(DuplicateKind.Exact)).Single();
        await _repository.SetKeeperAsync(set.Id, stranger);

        Assert.NotNull((await _repository.FindAsync(set.Id))!.Keeper);
    }

    [Fact]
    public async Task Resolved_AGroupKeptWholeIsNeverOfferedAgain()
    {
        // The answer for a burst of shots the app thought were duplicates.
        // Nothing is deleted, and being asked again on the next pass would be
        // the app failing to listen.
        AddPhoto(@"20230201\a.jpg", hash: "aaaa", phash: 0b1111);
        AddPhoto(@"20230201\b.jpg", hash: "bbbb", phash: 0b1111);
        await Find();

        int setId = (await _repository.GetAsync(DuplicateKind.Near)).Single().Id;
        await _repository.MarkResolvedAsync(setId, true);

        DuplicateScan again = await Find();

        Assert.Equal(0, again.NearSets);
        Assert.Empty(await _repository.GetAsync(DuplicateKind.Near));

        // And nothing was destroyed to achieve it.
        Assert.Equal(2, await _db.Assets.CountAsync());
    }

    [Fact]
    public async Task Resolved_DoesNotSilenceAPictureInSomeOtherGroup()
    {
        // Settling one group must not quietly settle a different photograph that
        // happens to be nearby in the library.
        // The second pair sits 36 bits away from the first, so they are two
        // groups rather than one - four bits apart and leader clustering would
        // rightly have swallowed all four into one.
        AddPhoto(@"20230201\a.jpg", hash: "aaaa", phash: 0b1111);
        AddPhoto(@"20230201\b.jpg", hash: "bbbb", phash: 0b1111);
        AddPhoto(@"20230201\c.jpg", hash: "cccc", phash: 0xFFFF_FFFF_0000_0000);
        AddPhoto(@"20230201\d.jpg", hash: "dddd", phash: 0xFFFF_FFFF_0000_0000);
        await Find();

        Assert.Equal(2, (await _repository.GetAsync(DuplicateKind.Near)).Count);

        DuplicateSetView first = (await _repository.GetAsync(DuplicateKind.Near))[0];
        await _repository.MarkResolvedAsync(first.Id, true);

        DuplicateScan again = await Find();

        Assert.Equal(1, again.NearSets);
    }

    private Task<DuplicateScan> Find() => new FindDuplicatesHandler(_repository).HandleAsync();

    private QuarantineDuplicatesHandler Quarantine() => new(_repository, _quarantine);

    private string WriteFile(string relativePath, string content)
    {
        string full = Path.Combine(_library, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>
    /// The digest some bytes actually have.
    /// </summary>
    /// <remarks>
    /// Setting a copy aside checks the file that arrived in the quarantine
    /// against the digest on the row before it deletes the library's original, so
    /// a test that writes a real file has to record the real answer for it. A
    /// convenient label like "aaaa" says two rows are duplicates of each other
    /// but nothing true about either file.
    /// </remarks>
    private static string Digest(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private int AddPhoto(
        string relativePath,
        string hash,
        ulong phash = 0,
        int width = 100,
        int height = 100)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1000,
            ModifiedUtc = s_when,
            CreatedUtc = s_when,
            IndexedUtc = s_when,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ContentHash = hash,
            PerceptualHash = new PerceptualHash(phash),
            ThumbnailName = $"{hash}-thumb.jpg",
            Width = width,
            Height = height,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return asset.Id;
    }

    public void Dispose()
    {
        _db.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
