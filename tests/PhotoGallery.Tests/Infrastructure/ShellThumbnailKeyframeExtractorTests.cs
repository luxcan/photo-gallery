using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The wait on a video's thumbnail handler, which is someone else's code and
/// may never return.
/// </summary>
/// <remarks>
/// A clip that makes a real handler hang cannot be made on demand, so the
/// handler is stood in for by work that blocks until the test lets it go.
/// </remarks>
public sealed class ShellThumbnailKeyframeExtractorTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AHandlerThatAnswers_GivesItsAnswer()
    {
        KeyframeReading reading = await ShellThumbnailKeyframeExtractor.OnStaThreadAsync(
            () => KeyframeReading.Undecodable, Generous, CancellationToken.None);

        Assert.Equal(KeyframeOutcome.Undecodable, reading.Outcome);
    }

    [Fact]
    public async Task TheHandlerRunsInASingleThreadedApartment()
    {
        ApartmentState seen = ApartmentState.Unknown;

        await ShellThumbnailKeyframeExtractor.OnStaThreadAsync(
            () =>
            {
                seen = Thread.CurrentThread.GetApartmentState();
                return KeyframeReading.Unavailable;
            },
            Generous,
            CancellationToken.None);

        Assert.Equal(ApartmentState.STA, seen);
    }

    [Fact]
    public async Task AHandlerThatNeverReturns_TimesOut()
    {
        // Told to the caller rather than answered, because what a timeout means
        // depends on whether the share is still there.
        using var release = new ManualResetEventSlim();
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(
                () => ShellThumbnailKeyframeExtractor.OnStaThreadAsync(
                    () =>
                    {
                        release.Wait();
                        return KeyframeReading.Unavailable;
                    },
                    TimeSpan.FromMilliseconds(200),
                    CancellationToken.None));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task AFileTheShareQuicklySaysIsThere_IsWrittenOff()
    {
        // A planted clip on a healthy share: the handler hung, the share did
        // not, so the clip is recorded and not opened again next scan.
        KeyframeReading reading = await ShellThumbnailKeyframeExtractor.WriteOffIfReachableAsync(
            () => true, Generous, CancellationToken.None);

        Assert.Equal(KeyframeOutcome.Undecodable, reading.Outcome);
        Assert.True(reading.IsSettled);
    }

    [Fact]
    public async Task AFileTheShareSaysIsGone_IsLeftToBeOfferedAgain()
    {
        KeyframeReading reading = await ShellThumbnailKeyframeExtractor.WriteOffIfReachableAsync(
            () => false, Generous, CancellationToken.None);

        Assert.Equal(KeyframeOutcome.Unavailable, reading.Outcome);
        Assert.False(reading.IsSettled);
    }

    [Fact]
    public async Task AShareThatStallsOnTheCheckToo_LeavesTheClipToBeOfferedAgain()
    {
        // The Wi-Fi dropped or the disks are spinning up: the handler timed out
        // because the share did, and writing the clip off would leave it blank
        // for good.
        using var release = new ManualResetEventSlim();
        try
        {
            KeyframeReading reading = await ShellThumbnailKeyframeExtractor.WriteOffIfReachableAsync(
                () =>
                {
                    release.Wait();
                    return true;
                },
                TimeSpan.FromMilliseconds(200),
                CancellationToken.None);

            Assert.Equal(KeyframeOutcome.Unavailable, reading.Outcome);
            Assert.False(reading.IsSettled);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task StopEndsTheWaitOnAHandlerThatNeverReturns()
    {
        using var release = new ManualResetEventSlim();
        using var stop = new CancellationTokenSource();
        try
        {
            Task<KeyframeReading> waiting = ShellThumbnailKeyframeExtractor.OnStaThreadAsync(
                () =>
                {
                    release.Wait();
                    return KeyframeReading.Unavailable;
                },
                Generous,
                stop.Token);

            await stop.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task AHandlerThatThrows_ThrowsToTheCaller()
    {
        // Rather than ending the process from a thread nobody is watching.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ShellThumbnailKeyframeExtractor.OnStaThreadAsync<KeyframeReading>(
                () => throw new InvalidOperationException("handler"),
                Generous,
                CancellationToken.None));
    }
}
