using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Infrastructure;

public sealed class FileSystemThumbnailStoreTests : IDisposable
{
    private readonly string _root;
    private readonly WorkingFolder _workingFolder;
    private readonly FileSystemThumbnailStore _store;

    public FileSystemThumbnailStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-thumbs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _workingFolder = new WorkingFolder(_root);
        _workingFolder.EnsureCreated();
        _store = new FileSystemThumbnailStore(_workingFolder);
    }

    [Fact]
    public async Task Save_WritesBothRenditionsSideBySide()
    {
        string name = await _store.SaveAsync(Thumbnail(1));

        Assert.True(File.Exists(_store.ResolveTilePath(name)));
        Assert.True(File.Exists(_store.ResolvePreviewPath(name)));
        Assert.Equal(
            Path.GetDirectoryName(_store.ResolveTilePath(name)),
            Path.GetDirectoryName(_store.ResolvePreviewPath(name)));
    }

    [Fact]
    public async Task Save_SpreadsALibraryOverManyDirectories()
    {
        // The whole point of sharding. Sequential ids once produced names that
        // all began "00", putting every file of a 16,225-asset library into a
        // single directory; a content hash spreads them evenly.
        for (int assetId = 1; assetId <= 300; assetId++)
        {
            await _store.SaveAsync(Thumbnail(assetId));
        }

        string[] shards = Directory.GetDirectories(_workingFolder.ThumbnailsPath);

        // 300 hashes over 256 shards fill about 176 of them once collisions are
        // allowed for; the assertion only has to catch the old behaviour, which
        // was one directory for the entire library.
        Assert.True(shards.Length > 120, $"300 pictures landed in only {shards.Length} directories");
        int busiest = shards.Max(s => Directory.GetFiles(s).Length);
        Assert.True(busiest < 30, $"one directory held {busiest} files");
    }

    [Fact]
    public async Task Save_TwiceForTheSamePicture_Overwrites()
    {
        // Also what happens when two sources hold identical copies: one pair of
        // files, not two, because the name comes from the picture.
        string first = await _store.SaveAsync(Thumbnail(7));
        string second = await _store.SaveAsync(Thumbnail(7));

        Assert.Equal(first, second);
        Assert.Single(
            Directory.GetFiles(Path.GetDirectoryName(_store.ResolveTilePath(first))!, "*.jpg"),
            file => !file.Contains("-p", StringComparison.Ordinal));
    }

    [Fact]
    public void Exists_IsFalseForNothing()
    {
        Assert.False(_store.Exists(null));
        Assert.False(_store.Exists(string.Empty));
        Assert.False(_store.Exists("   "));
    }

    [Fact]
    public async Task Exists_IsFalseWhenTheFileHasGone()
    {
        // A row's name is a claim, not proof: working folders get copied and
        // cleaned without their index.
        string name = await _store.SaveAsync(Thumbnail(42));
        Assert.True(_store.Exists(name));

        File.Delete(_store.ResolveTilePath(name));

        Assert.False(_store.Exists(name));
    }

    [Fact]
    public async Task TryDelete_RemovesBothRenditionsAndSaysSo()
    {
        string name = await _store.SaveAsync(Thumbnail(9));

        Assert.True(_store.TryDelete(name));

        Assert.False(File.Exists(_store.ResolveTilePath(name)));
        Assert.False(File.Exists(_store.ResolvePreviewPath(name)));
    }

    [Fact]
    public void TryDelete_IsTrueForSomethingThatIsNotThere()
    {
        Assert.True(_store.TryDelete(null));
        Assert.True(_store.TryDelete("ffffffffffffffffffffffffffffffff.jpg"));
    }

    [Fact]
    public async Task TryDelete_IsFalseWhileSomethingElseHoldsTheFile()
    {
        // Detaching deletes a record's files before its row, so it has to be told
        // the truth. This used to be swallowed and counted as reclaimed.
        string name = await _store.SaveAsync(Thumbnail(11));

        using var held = File.Open(
            _store.ResolveTilePath(name), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(_store.TryDelete(name));
        Assert.True(File.Exists(_store.ResolveTilePath(name)));
    }

    [Fact]
    public async Task TryDelete_StillRemovesThePreviewWhenTheTileIsHeld()
    {
        // The two renditions are attempted independently: a held tile must not
        // strand its preview, which is seven times the size.
        string name = await _store.SaveAsync(Thumbnail(12));

        using var held = File.Open(
            _store.ResolveTilePath(name), FileMode.Open, FileAccess.Read, FileShare.None);
        _store.TryDelete(name);

        Assert.False(File.Exists(_store.ResolvePreviewPath(name)));
    }

    [Fact]
    public async Task ListStoredNames_FindsRenditionsTheIndexNeverNamed()
    {
        // One name per picture: the preview belongs to its tile rather than being
        // a name of its own.
        string first = await _store.SaveAsync(Thumbnail(1));
        string second = await _store.SaveAsync(Thumbnail(2));

        IReadOnlyCollection<string> names = _store.ListStoredNames();

        Assert.Equal(2, names.Count);
        Assert.Contains(first, names);
        Assert.Contains(second, names);
    }

    [Fact]
    public async Task RemoveEmptyShards_RemovesOnlyTheOnesThatAreEmpty()
    {
        // One shard, both behaviours in sequence: two seeds could land in the
        // same directory and make the comparison meaningless.
        string name = await _store.SaveAsync(Thumbnail(3));
        string shard = Path.GetDirectoryName(_store.ResolveTilePath(name))!;

        _store.RemoveEmptyShards();
        Assert.True(Directory.Exists(shard));

        _store.TryDelete(name);
        _store.RemoveEmptyShards();

        Assert.False(Directory.Exists(shard));
        Assert.True(Directory.Exists(_workingFolder.ThumbnailsPath));
    }

    /// <summary>
    /// Distinct hashes per id, spread the way real digests are - a sequential
    /// number would not exercise the sharding at all.
    /// </summary>
    private static GeneratedThumbnail Thumbnail(int seed) =>
        new([1, 2, 3], [4, 5, 6, 7], 1600, 1200, null, new PerceptualHash(0),
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(seed))));

    public void Dispose()
    {
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
