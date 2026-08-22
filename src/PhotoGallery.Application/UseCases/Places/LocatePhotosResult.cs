namespace PhotoGallery.Application.UseCases.Places;

/// <summary>How far working out where photographs were taken got.</summary>
/// <param name="Examined">Photographs the pass settled an answer for.</param>
/// <param name="Located">
/// Photographs that carried coordinates. The rest is not a failure - most
/// cameras have no receiver - which is why the summary says so rather than
/// reporting it as something that went wrong.
/// </param>
/// <param name="Named">
/// Of those located, how many were near enough to somewhere the gazetteer knows
/// to be given its name.
/// </param>
/// <param name="Unreadable">
/// Files that would not open. Deliberately left unmarked so they are offered
/// again, unlike the ones that simply had no coordinates.
/// </param>
/// <param name="UnreachableSources">
/// Roots that could not be listed. Their photographs were not examined at all
/// and nothing about them was written.
/// </param>
public sealed record LocatePhotosResult(
    int Examined,
    int Located,
    int Named,
    int Unreadable,
    IReadOnlyList<string> UnreachableSources,
    TimeSpan Elapsed,
    bool Cancelled)
{
    public static LocatePhotosResult Nothing(TimeSpan elapsed) =>
        new(0, 0, 0, 0, [], elapsed, false);

    public string Summary
    {
        get
        {
            string away = UnreachableSources.Count == 0
                ? string.Empty
                : $" {Away()} could not be reached, so its photographs were left alone.";

            if (Examined == 0)
            {
                return UnreachableSources.Count > 0
                    ? $"Nothing could be examined.{away}"
                    : "Every photograph has already been placed.";
            }

            string headline = Cancelled
                ? $"Stopped after examining {Examined:N0} photographs, "
                  + $"naming {Named:N0}. The rest are still there for next time."
                : $"{Named:N0} photographs placed out of {Examined:N0} examined, in {Minutes()}. "
                  + Unplaced();

            return Unreadable == 0
                ? headline + away
                : headline + $" {Unreadable:N0} would not open and will be tried again." + away;
        }
    }

    /// <summary>
    /// Why most photographs got no place, said plainly so it does not read as a
    /// fault.
    /// </summary>
    private string Unplaced() => (Located - Named) switch
    {
        0 => $"The other {Examined - Located:N0} carry no location.",
        int far => $"{far:N0} more carry a location too far from anywhere named. "
                   + $"The other {Examined - Located:N0} carry none at all.",
    };

    private string Away() => UnreachableSources.Count == 1
        ? UnreachableSources[0]
        : $"{UnreachableSources.Count:N0} sources";

    private string Minutes() => Elapsed.TotalMinutes >= 1d
        ? $"{Elapsed.TotalMinutes:N0} min"
        : $"{Elapsed.TotalSeconds:N0}s";
}

/// <summary>How far the pass has got, for the screen to show.</summary>
public readonly record struct LocatePhotosProgress(
    int Done, int Total, int Named, TimeSpan Elapsed)
{
    public double Fraction => Total == 0 ? 0d : (double)Done / Total;

    /// <summary>
    /// Roughly how much longer. Measured at 738 ms a file over the share, so a
    /// first run on a full library is tens of minutes and needs an end in sight
    /// for the same reason the describing pass does.
    /// </summary>
    public TimeSpan? Remaining => Done <= 0 || Done >= Total
        ? null
        : TimeSpan.FromTicks(Elapsed.Ticks / Done * (Total - Done));
}
