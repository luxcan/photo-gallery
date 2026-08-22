using PhotoGallery.Application.UseCases.Videos;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// What the screen is told while the video pass runs, and what it says when it
/// stops.
/// </summary>
/// <remarks>
/// The estimate matters more here than on any other pass. Nobody knows what this
/// one costs in advance - it seeks rather than reading each file through - so
/// the number measured from the run in front of the user is the only one there
/// is, and it is what decides whether somebody stops a job that was nearly done.
/// </remarks>
public sealed class VideoProgressTests
{
    [Fact]
    public void Fraction_FillsTheBarInProportion()
    {
        var report = new VideoProgress(250, 1000, 240, 10, TimeSpan.FromMinutes(1));

        Assert.Equal(0.25d, report.Fraction, 6);
    }

    [Fact]
    public void Fraction_OfNothingToDoIsNotADivideByZero()
    {
        var report = new VideoProgress(0, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(0d, report.Fraction);
    }

    [Fact]
    public void Remaining_EstimatesFromWhatHasBeenSpentSoFar()
    {
        // A quarter done in one minute means about three minutes left.
        var report = new VideoProgress(250, 1000, 250, 0, TimeSpan.FromMinutes(1));

        Assert.NotNull(report.Remaining);
        Assert.Equal(3d, report.Remaining!.Value.TotalMinutes, 1);
    }

    [Fact]
    public void Remaining_SaysNothingBeforeThereIsAnythingToGoOn()
    {
        // The report made before any work starts, so the screen can name the
        // total. Dividing by what it has done would throw.
        var report = new VideoProgress(0, 1000, 0, 0, TimeSpan.Zero);

        Assert.Null(report.Remaining);
    }

    [Fact]
    public void Remaining_SaysNothingOnceItIsFinished()
    {
        var report = new VideoProgress(1000, 1000, 990, 10, TimeSpan.FromHours(2));

        Assert.Null(report.Remaining);
    }

    [Fact]
    public void Summary_OfNothingOutstandingDoesNotReadAsAFailure()
    {
        var result = new VideoBuildResult(0, 0, 0, 0, TimeSpan.FromSeconds(2), false);

        Assert.Equal("Every video already has a picture on it.", result.Summary);
    }

    [Fact]
    public void Summary_SaysWhatIsLeftAfterStopping()
    {
        var result = new VideoBuildResult(
            1000, 240, 0, 0, TimeSpan.FromMinutes(30), Cancelled: true);

        Assert.Contains("240", result.Summary, StringComparison.Ordinal);
        Assert.Contains("still there for next time", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_PromisesNotToOpenADeadContainerAgain()
    {
        // The one thing a person needs told about a clip Windows has no codec
        // for: it is not coming back, so there is nothing to wait for.
        var result = new VideoBuildResult(100, 97, 3, 0, TimeSpan.FromMinutes(5), false);

        Assert.Contains("3 would not open", result.Summary, StringComparison.Ordinal);
        Assert.Contains("not be tried again", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_LeavesFailuresUnmentionedWhenThereAreNone()
    {
        var result = new VideoBuildResult(100, 100, 0, 0, TimeSpan.FromMinutes(5), false);

        Assert.DoesNotContain("would not open", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_PromisesToTryAgainForWhatItCouldNotReach()
    {
        // The opposite promise to the one above, and the difference matters: a
        // clip that could not be reached was not written off, so the count must
        // not read like damage.
        var result = new VideoBuildResult(100, 93, 0, 7, TimeSpan.FromMinutes(5), false);

        Assert.Contains("7 could not be reached", result.Summary, StringComparison.Ordinal);
        Assert.Contains("will be tried again", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("will not be tried again", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_TellsTheTwoKindsOfFailureApart()
    {
        var result = new VideoBuildResult(100, 90, 3, 7, TimeSpan.FromMinutes(5), false);

        Assert.Contains("3 would not open", result.Summary, StringComparison.Ordinal);
        Assert.Contains("7 could not be reached", result.Summary, StringComparison.Ordinal);
    }
}
