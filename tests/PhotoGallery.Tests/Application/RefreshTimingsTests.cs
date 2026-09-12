using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Refresh;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Application.UseCases.Videos;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// That a finished scan says where its time went.
/// </summary>
/// <remarks>
/// Every phase has always measured itself and the result printed one total, so a
/// scan that took thirty-seven minutes said only that it took thirty-seven
/// minutes. Nothing could be measured before and after a change, which is the
/// same as saying nothing could be optimised on purpose - the first thing asked
/// of this pipeline was whether it was worth putting the models on the graphics
/// card, and there was no way to answer it except by guessing.
///
/// <para>The property worth guarding is not the wording but the arithmetic: the
/// lines have to add up to the total, or a reader cannot tell a phase that was
/// slow from a phase that was never counted.</para>
/// </remarks>
public sealed class RefreshTimingsTests
{
    [Fact]
    public void EveryPhaseThatRanGetsALine()
    {
        IReadOnlyList<string> lines = Finished().Timings;

        Assert.Contains(lines, line => line.StartsWith("crawl", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("pictures", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("places", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("describing", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("videos", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("faces", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("albums", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("total", StringComparison.Ordinal));
    }

    [Fact]
    public void APhaseThatNeverRanIsAbsentRatherThanZero()
    {
        // "We never got that far" and "there was nothing to do" are the whole
        // distinction this record is built around. A line of zeroes loses it.
        RefreshResult stopped = Finished() with { Faces = null, Videos = null };

        Assert.DoesNotContain(
            stopped.Timings,
            line => line.StartsWith("faces", StringComparison.Ordinal));
        Assert.DoesNotContain(
            stopped.Timings,
            line => line.StartsWith("videos", StringComparison.Ordinal));
    }

    [Fact]
    public void WhatThePhasesDoNotOwnIsReportedRatherThanLost()
    {
        // Applying held answers is not separately timed, and neither is the work
        // between phases. Without a remainder the lines quietly fail to add up,
        // and a reader cannot tell a slow phase from an uncounted one.
        RefreshResult result = Finished() with { Elapsed = TimeSpan.FromSeconds(200) };

        Assert.Contains(result.Timings, line => line.StartsWith("other", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunWithNothingUnaccountedForDoesNotInventARemainder()
    {
        RefreshResult tight = Finished() with { Elapsed = TimeSpan.FromSeconds(66) };

        Assert.DoesNotContain(
            tight.Timings,
            line => line.StartsWith("other", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTotalIsLast()
    {
        // It is the line somebody looks for first and the one they read last, and
        // it is what the lines above it have to add up to.
        Assert.StartsWith("total", Finished().Timings[^1], StringComparison.Ordinal);
    }

    /// <summary>A run in which every phase did something, adding up to 66 seconds.</summary>
    private static RefreshResult Finished() =>
        new(
            Scans: [new ScanResult(1, "C:\\photos", 10, 1, 0, 0, TimeSpan.FromSeconds(6), false)],
            Generated: new ThumbnailBuildResult(10, 10, 0, TimeSpan.FromSeconds(20), false),
            Described: new ContentIndexResult(10, 10, 0, TimeSpan.FromSeconds(15), false, false),
            Located: new LocatePhotosResult(10, 10, 10, 0, [], TimeSpan.FromSeconds(5), false),
            Videos: new VideoBuildResult(2, 2, 0, 0, TimeSpan.FromSeconds(12), false),
            Faces: new FaceDetectionResult(10, 10, 6, 0, TimeSpan.FromSeconds(7), false, false),
            Answers: null,
            Collected: new AlbumsResult(0, 0, 0, TimeSpan.FromSeconds(1), false),
            Elapsed: TimeSpan.FromSeconds(66),
            WasCancelled: false);
}
