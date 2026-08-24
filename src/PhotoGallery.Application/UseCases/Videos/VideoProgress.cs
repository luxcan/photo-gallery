namespace PhotoGallery.Application.UseCases.Videos;

/// <summary>How far the keyframe pass has got.</summary>
public readonly record struct VideoProgress(
    int Done, int Total, int Prepared, int Failed, TimeSpan Elapsed)
{
    public double Fraction => Total == 0 ? 0d : (double)Done / Total;

    /// <summary>
    /// Roughly how much longer, from how long it has taken so far.
    /// </summary>
    /// <remarks>
    /// Worth showing here more than anywhere else in the app. Nobody knows what
    /// this pass costs - it seeks rather than reads through, so the 11.6 hours a
    /// full read of these bytes would take is an upper bound and not a
    /// prediction - and an estimate measured from the run in front of you is the
    /// only honest number available. It is also the number that stops somebody
    /// abandoning a run that was nearly finished.
    /// </remarks>
    public TimeSpan? Remaining => Done <= 0 || Done >= Total
        ? null
        : TimeSpan.FromTicks(Elapsed.Ticks / Done * (Total - Done));
}
