namespace PhotoGallery.Application.UseCases.Search;

/// <summary>How far describing the library got.</summary>
/// <param name="Renditions">Distinct previews the pass set out to read.</param>
/// <param name="Described">
/// Photographs now searchable, which is more than the number of previews read
/// wherever duplicates share one.
/// </param>
/// <param name="Failed">
/// Previews that would not decode. Left unmarked rather than written off: a
/// preview is this app's own file and preparing the picture again remakes it.
/// </param>
public sealed record ContentIndexResult(
    int Renditions,
    int Described,
    int Failed,
    TimeSpan Elapsed,
    bool Cancelled,
    bool ModelsMissing)
{
    public static ContentIndexResult WithoutModels(TimeSpan elapsed) =>
        new(0, 0, 0, elapsed, false, true);

    public string Summary => ModelsMissing
        ? "The search models are not installed yet, so nothing was described."
        : Renditions == 0
            ? "Every picture has already been described."
            : Cancelled
                ? $"Stopped after describing {Described:N0} pictures. "
                  + "The rest are still there for next time."
                : Failed == 0
                    ? $"{Described:N0} pictures described in {Minutes()}."
                    : $"{Described:N0} pictures described in {Minutes()}. "
                      + $"{Failed:N0} previews would not open and will be tried again.";

    private string Minutes() => Elapsed.TotalMinutes >= 1d
        ? $"{Elapsed.TotalMinutes:N0} min"
        : $"{Elapsed.TotalSeconds:N0}s";
}

/// <summary>How far the pass has got, for the screen to show.</summary>
public readonly record struct ContentIndexProgress(
    int Read, int Total, int Described, int Failed, TimeSpan Elapsed)
{
    public double Fraction => Total == 0 ? 0d : (double)Read / Total;

    /// <summary>
    /// Roughly how much longer, from how long it has taken so far.
    /// </summary>
    /// <remarks>
    /// Worth showing because this pass takes the best part of an hour, and a bar
    /// with no end in sight is what makes someone stop a run that was nearly
    /// finished.
    /// </remarks>
    public TimeSpan? Remaining => Read <= 0 || Read >= Total
        ? null
        : TimeSpan.FromTicks(Elapsed.Ticks / Read * (Total - Read));
}
