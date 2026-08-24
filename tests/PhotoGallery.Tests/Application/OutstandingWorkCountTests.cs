using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Search;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// How much the two optional passes still have to look at.
/// </summary>
/// <remarks>
/// Counted so the screens can say "installed, and not applied yet" as a fact.
/// The tempting shortcut - no faces in the library means it has not been looked
/// at - is wrong on a library of landscapes, which really can be finished and
/// hold none; a notice built on that would offer a scan that changes nothing,
/// for ever.
///
/// <para>What matters here is that these agree with the candidate queries the
/// passes themselves use. A count that disagrees would either nag about work
/// that will never be offered, or go quiet while work is outstanding.</para>
/// </remarks>
public sealed class OutstandingWorkCountTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;


    public OutstandingWorkCountTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-counts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();

        _index = new SqliteLibraryIndex(_db);
    }

    [Fact]
    public async Task APreparedPictureNobodyHasLookedAtIsWaitingForBothPasses()
    {
        Add("a.jpg", "aa11.jpg");

        LibraryCounts counts = await _index.GetCountsAsync();

        Assert.Equal(1, counts.AwaitingFaces);
        Assert.Equal(1, counts.AwaitingDescription);
    }

    [Fact]
    public async Task LookedAtAndFoundNothingIsNotWaiting()
    {
        // The state the shortcut gets wrong: looked at, no faces recorded. The
        // library has nothing to show and nothing left to do.
        int id = Add("landscape.jpg", "bb22.jpg");
        Asset asset = await _db.Assets.FirstAsync(a => a.Id == id);
        asset.FacesDetectedUtc = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
        await _db.SaveChangesAsync();

        LibraryCounts counts = await _index.GetCountsAsync();

        Assert.Equal(0, counts.Faces);
        Assert.Equal(0, counts.AwaitingFaces);
    }

    [Fact]
    public async Task ADescribedPictureIsNotWaitingToBeDescribed()
    {
        int id = Add("cake.jpg", "cc33.jpg");
        _db.PhotoContent.Add(new PhotoContent
        {
            AssetId = id,
            Vector = new ContentEmbedding(new float[ContentEmbedding.Dimensions]),
            IndexedUtc = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
            ThumbnailName = "cc33.jpg",
        });
        await _db.SaveChangesAsync();

        LibraryCounts counts = await _index.GetCountsAsync();

        Assert.Equal(0, counts.AwaitingDescription);
        Assert.Equal(1, counts.AwaitingFaces);
    }

    [Fact]
    public async Task WhatNoPassWillBeOfferedIsNotCountedAsWaiting()
    {
        // Both passes read the small copy, so a picture without one is not work
        // they are waiting on - the preparing pass is.
        Add("unprepared.jpg", thumbnailName: null);

        // And a copy set aside as redundant is out of the library entirely.
        int quarantined = Add("copy.jpg", "dd44.jpg");
        Asset asset = await _db.Assets.FirstAsync(a => a.Id == quarantined);
        asset.QuarantinedUtc = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
        await _db.SaveChangesAsync();

        LibraryCounts counts = await _index.GetCountsAsync();

        Assert.Equal(0, counts.AwaitingFaces);
        Assert.Equal(0, counts.AwaitingDescription);
    }

    [Fact]
    public async Task AVideoWithAPictureOnItIsWaitingForFacesButNotForDescribing()
    {
        // The face pass reads keyframes as well as photographs; the describing
        // pass is photographs only.
        Add("clip.mp4", "ee55.jpg", kind: AssetKind.Video);

        LibraryCounts counts = await _index.GetCountsAsync();

        Assert.Equal(1, counts.AwaitingFaces);
        Assert.Equal(0, counts.AwaitingDescription);
    }

    private int Add(
        string relativePath,
        string? thumbnailName = null,
        AssetKind kind = AssetKind.Photo)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024,
            ModifiedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IndexedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Kind = kind,
            Status = AssetStatus.Ready,
            ThumbnailName = thumbnailName,
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
            // A temporary folder left behind is not a failed test.
        }
    }
}
