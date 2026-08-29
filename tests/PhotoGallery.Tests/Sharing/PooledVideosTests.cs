using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A video's frames crossing the pool, and the one thing that makes them
/// different from a photograph's.
/// </summary>
/// <remarks>
/// A photograph's rendition is named after a hash of its bytes, which is exactly
/// what the receiving machine is trying to avoid reading - so it has to be told.
/// A video's frame is named from the path, the length, the modified time and the
/// ordinal, all of which its own crawl collected for free. So it works the names
/// out, and the manifest carries only what it cannot compute.
/// </remarks>
public sealed class PooledVideosTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task AClipEndsUpWithAPosterAndItsFramesWithoutBeingToldTheirNames()
    {
        Asset clip = Clip(Mum, @"2019\holiday.mp4");
        Dad.Photo(@"2019\holiday.mp4");

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(1, taken.Filled);

        // The poster is on the row, and it is the name this machine computed.
        Asset his = Dad.Db.Assets.AsNoTracking().Single();
        Assert.Equal(Poster(clip), his.ThumbnailName);
        Assert.True(Dad.Holds(Poster(clip)));
    }

    [Fact]
    public async Task ItsDurationAndDimensionsAndFramePositionsComeFromTheManifest()
    {
        // The half the receiving machine cannot work out: how long the clip is,
        // how many frames there are and where in it each was taken from.
        Clip(Mum, @"2019\holiday.mp4");
        Dad.Photo(@"2019\holiday.mp4");

        await Mum.Pooling.HandleAsync();
        await Dad.Pooling.HandleAsync();

        Asset his = Dad.Db.Assets.AsNoTracking().Single();
        Assert.Equal(TimeSpan.FromMinutes(3), his.Duration);
        Assert.Equal(1920, his.Width);

        List<VideoKeyframe> frames =
            [.. Dad.Db.VideoKeyframes.AsNoTracking().OrderBy(frame => frame.Ordinal)];

        Assert.Equal(3, frames.Count);
        Assert.Equal(TimeSpan.Zero, frames[0].Position);
        Assert.Equal(TimeSpan.FromMinutes(1), frames[1].Position);
        Assert.Equal(TimeSpan.FromMinutes(2), frames[2].Position);
    }

    [Fact]
    public async Task TheFrameNamesAreComputedRatherThanCarried()
    {
        // Asserted against the manifest itself: it says where each frame came
        // from and never what it is called.
        Clip(Mum, @"2019\holiday.mp4");
        await Mum.Pooling.HandleAsync();

        IReadOnlyList<PreparedSet> manifests = await Dad.Pool.FetchAsync();
        PreparedFact fact = Assert.Single(manifests.SelectMany(set => set.Facts));

        Assert.Equal(3, fact.Keyframes.Count);
        Assert.All(fact.Keyframes, still => Assert.True(still.Position >= TimeSpan.Zero));

        // And the name the receiving machine works out is the one the sending
        // machine actually wrote.
        string computed = RenditionName.For(VideoKeyframeIdentity.For(
            fact.Photo.RelativePath, fact.Length, fact.ModifiedUtc, 1));

        Assert.Equal(
            Mum.Db.VideoKeyframes.AsNoTracking().Single(f => f.Ordinal == 1).ThumbnailName,
            computed);
    }

    [Fact]
    public async Task EveryFrameIsFetchedAndNotJustThePoster()
    {
        Asset clip = Clip(Mum, @"2019\holiday.mp4");
        Dad.Photo(@"2019\holiday.mp4");

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(3, taken.Fetched);

        for (int ordinal = 0; ordinal < 3; ordinal++)
        {
            Assert.True(Dad.Holds(Frame(clip, ordinal)), $"frame {ordinal} did not arrive");
        }
    }

    [Fact]
    public async Task ATurnMergedBeforeItsPictureArrivesIsAppliedWhenThePictureLands()
    {
        // Locally, a rendition that cannot be read leaves the library exactly as
        // it was - which is right, and silent disaster here: a fresh machine
        // merges every turn before it owns a single rendition, drops all of them
        // and then publishes its own upright answer as a competing one.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");

        Asset turned = Mum.Db.Assets.Single();
        turned.Rotation = 90;
        turned.RotatedUtc = Monday;
        turned.RotatedBy = Mum.MachineId;
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();

        // Dad has the row and no picture at all, so the turn cannot be applied.
        Dad.Photo(@"2019\a.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(0, Dad.Db.Assets.AsNoTracking().Single().Rotation);
        Assert.Single(Dad.Db.HeldDecisions.Where(h => h.Kind == HeldDecisionKind.Turn));

        // A different machine, which never turned it, leaves the upright picture
        // in the pool. Dad fetches it, and the turn he was holding lands.
        Library ana = _house.Add("Ana's laptop");
        ana.Prepared(@"2019\a.jpg", "aa11.jpg");
        await ana.Pooling.HandleAsync();

        await Dad.Pooling.HandleAsync();

        Assert.True(Dad.Holds("aa11.jpg"));
        Assert.Equal(90, Dad.Db.Assets.AsNoTracking().Single().Rotation);
        Assert.Empty(Dad.Db.HeldDecisions.Where(h => h.Kind == HeldDecisionKind.Turn));
    }

    // ------------------------------------------------------------------ setup

    /// <summary>A video prepared as the keyframe pass leaves one: a poster and three frames.</summary>
    private static Asset Clip(Library library, string relativePath)
    {
        Asset clip = library.Photo(relativePath);

        clip.Kind = AssetKind.Video;
        clip.Status = AssetStatus.Ready;
        clip.Duration = TimeSpan.FromMinutes(3);
        clip.Width = 1920;
        clip.Height = 1080;
        clip.ThumbnailName = Poster(clip);
        library.Db.SaveChanges();

        for (int ordinal = 0; ordinal < 3; ordinal++)
        {
            string name = Frame(clip, ordinal);

            library.Db.VideoKeyframes.Add(new VideoKeyframe
            {
                AssetId = clip.Id,
                Ordinal = ordinal,
                Position = TimeSpan.FromMinutes(ordinal),
                ThumbnailName = name,
            });

            library.WritePicture(name);
        }

        library.Db.SaveChanges();
        library.Db.ChangeTracker.Clear();
        return clip;
    }

    /// <summary>What the pass would have called one of a clip's frames.</summary>
    private static string Frame(Asset clip, int ordinal) =>
        RenditionName.For(VideoKeyframeIdentity.For(
            clip.RelativePath, clip.Length, clip.ModifiedUtc, ordinal));

    private static string Poster(Asset clip) => Frame(clip, 0);

    public void Dispose() => _house.Dispose();
}
