using PhotoGallery.Application.UseCases.Search;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// What the screen is told while a long pass runs.
/// </summary>
/// <remarks>
/// Worth its own tests because this is the only part of an hour-long pass the
/// user can see, and every way of getting it wrong looks like the pass itself
/// having stopped.
/// </remarks>
public sealed class ContentIndexProgressTests
{
    [Fact]
    public void Fraction_FillsTheBarInProportion()
    {
        var report = new ContentIndexProgress(250, 1000, 250, 0, TimeSpan.FromMinutes(1));

        Assert.Equal(0.25d, report.Fraction, 6);
    }

    [Fact]
    public void Fraction_OfNothingToDoIsNotADivideByZero()
    {
        var report = new ContentIndexProgress(0, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(0d, report.Fraction);
    }

    [Fact]
    public void Remaining_EstimatesFromWhatHasBeenSpentSoFar()
    {
        // A quarter done in one minute means about three minutes left.
        var report = new ContentIndexProgress(250, 1000, 250, 0, TimeSpan.FromMinutes(1));

        Assert.NotNull(report.Remaining);
        Assert.Equal(3d, report.Remaining!.Value.TotalMinutes, 1);
    }

    [Fact]
    public void Remaining_SaysNothingBeforeThereIsAnythingToGoOn()
    {
        // The first report of a run carries no work done, and dividing by it
        // would either throw or promise infinity.
        var report = new ContentIndexProgress(0, 1000, 0, 0, TimeSpan.Zero);

        Assert.Null(report.Remaining);
    }

    [Fact]
    public void Remaining_SaysNothingOnceItIsFinished()
    {
        var report = new ContentIndexProgress(1000, 1000, 1000, 0, TimeSpan.FromMinutes(4));

        Assert.Null(report.Remaining);
    }
}
