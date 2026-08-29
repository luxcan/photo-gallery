using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Where the pooled pictures and the merged turns meet, which is the only part
/// of either that is not obvious.
/// </summary>
/// <remarks>
/// A turn rewrites both renditions in place under a name derived from the
/// original's bytes, which the turn does not change. So the same name means two
/// different pictures across the house, and every rule here exists because of
/// that one fact.
/// </remarks>
public sealed class PooledTurnsTests : IDisposable
{
    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task AMachineThatHasMergedATurnStillFetchesThePictureAndTurnsItItself()
    {
        // The asymmetry, and it is deliberate: the pool only ever holds the
        // as-generated rendition, so forbidding the fetch would leave exactly the
        // photographs somebody cared enough to straighten falling back to an
        // hour of reading originals.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.HandleAsync();

        // Dad has indexed the photograph and already knows it needs turning.
        Asset his = Dad.Photo(@"2019\a.jpg");
        his.Rotation = 90;
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(1, taken.Filled);
        Assert.True(Dad.Holds("aa11.jpg"));
        Assert.Equal(90, Dad.Db.Assets.AsNoTracking().Single().Rotation);
    }

    [Fact]
    public async Task AndHeDoesNotHandThatTurnedPictureBackToThePool()
    {
        // Once he has turned it, the file under that name is his sideways one.
        // Offering it would put a sideways tile under a name that means upright
        // everywhere else.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.HandleAsync();

        Asset his = Dad.Photo(@"2019\a.jpg");
        his.Rotation = 90;
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        await Dad.Pooling.HandleAsync();

        // A third machine reads the pool and finds Mum's upright picture, not
        // Dad's turned one - there is only ever one file under that name.
        IReadOnlyList<PhotoGallery.Domain.Sharing.PreparedSet> manifests =
            await Mum.Pool.FetchAsync();

        Assert.All(
            manifests.SelectMany(set => set.Facts),
            fact => Assert.NotEqual("aa11.jpg", fact.ThumbnailName));
    }

    [Fact]
    public async Task MergingATurnWritesNoOriginal()
    {
        // Four laptops merging one decision would queue for an exclusive write
        // on one file on the share, and each that won would change its modified
        // time - invalidating that photograph's rendition for everybody,
        // repeatedly. The person who turned it has already told the file.
        string source = Path.Combine(_house.SharedFolder, "..", "photos");
        Directory.CreateDirectory(source);

        string original = Path.Combine(source, "a.jpg");
        await File.WriteAllTextAsync(original, "the original bytes");
        DateTime before = File.GetLastWriteTimeUtc(original);

        Mum.Prepared(@"2019\a.jpg", "aa11.jpg", rotation: 90);
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(before, File.GetLastWriteTimeUtc(original));
        Assert.Equal("the original bytes", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task AnExchangeStoppedHalfwayResumesRatherThanStartingOver()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Mum.Prepared(@"2019\b.jpg", "bb22.jpg");
        Dad.Photo(@"2019\a.jpg");
        Dad.Photo(@"2019\b.jpg");

        await Mum.Pooling.HandleAsync();

        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        PoolResult stopped = await Dad.Pooling.HandleAsync(null, stop.Token);

        Assert.True(stopped.WasCancelled || stopped.Filled == 0);
        Assert.Contains("carries on from here", stopped.Summary, StringComparison.OrdinalIgnoreCase);

        // And the next run finishes it.
        PoolResult finished = await Dad.Pooling.HandleAsync();

        Assert.Equal(2, finished.Filled);
        Assert.True(Dad.Holds("aa11.jpg"));
        Assert.True(Dad.Holds("bb22.jpg"));
    }

    [Fact]
    public async Task APhotographWhoseBytesChangedGetsANewNameRatherThanOverwritingTheOld()
    {
        // Nothing in the pool is ever overwritten, so "latest" needs no version
        // at all - it is whichever names are there now.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.HandleAsync();

        // Re-saved in place: new bytes, so a new content hash and a new name.
        Asset again = Mum.Db.Assets.Single();
        again.ThumbnailName = "cc33.jpg";
        again.ContentHash = "cc33";
        again.Length += 512;
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();
        Mum.WritePicture("cc33.jpg");

        await Mum.Pooling.HandleAsync();

        IReadOnlyCollection<string> pooled = await Mum.Pool.NamesAsync();

        Assert.Contains("aa11.jpg", pooled);
        Assert.Contains("cc33.jpg", pooled);
    }

    [Fact]
    public async Task AFileReSavedInPlaceDoesNotTakeTheRenditionOfTheBytesItNoLongerHas()
    {
        // The path is the same and the picture is not. Taking it would show the
        // old photograph under the new file, silently.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.HandleAsync();

        Asset his = Dad.Photo(@"2019\a.jpg");
        his.Length += 512;
        his.ModifiedUtc = his.ModifiedUtc.AddDays(1);
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, taken.Filled);
        Assert.Equal(1, taken.Mismatched);
        Assert.Null(Dad.Db.Assets.AsNoTracking().Single().ThumbnailName);
    }

    [Fact]
    public async Task ThePooledPictureFolderCannotBecomeAPhotoSource()
    {
        // Sharing writes .jpg files into a folder tree. A scan would index them
        // as photographs and grow the library a second copy of itself on every
        // refresh - so the rule runs in both directions.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.HandleAsync();

        string thumbs = Path.Combine(_house.SharedFolder, SharedFolderPool.ThumbsFolder);
        Assert.True(Directory.Exists(thumbs), "the pool wrote no pictures to refuse");

        var adding = new AddPhotoSourceHandler(Mum.Index, new NeverAppOwned());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adding.HandleAsync(thumbs));

        // And the folder above it, which is the half that gets left out.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adding.HandleAsync(_house.SharedFolder));
    }

    /// <summary>A working folder that claims nothing, so only the shared-folder rule can refuse.</summary>
    private sealed class NeverAppOwned : PhotoGallery.Application.Ports.IWorkingFolder
    {
        public string Root => Path.GetTempPath();

        public string DatabasePath => Path.Combine(Root, "index.db");

        public string ThumbnailsPath => Path.Combine(Root, "thumbs");

        public string ModelsPath => Path.Combine(Root, "models");

        public string QuarantinePath => Path.Combine(Root, "quarantine");

        public string LogsPath => Path.Combine(Root, "logs");

        public void EnsureCreated()
        {
        }

        public bool IsAppOwned(string path) => false;
    }

    public void Dispose() => _house.Dispose();
}
