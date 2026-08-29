namespace PhotoGallery.Application.UseCases.Collections;

/// <summary>
/// What the grouping phase did.
/// </summary>
/// <param name="Proposed">How many occasions are on offer after this run.</param>
/// <param name="Grouped">How many photographs are in them.</param>
/// <param name="Considered">
/// How many photographs could be placed on a timeline at all. Everything else
/// in the library carries no capture date, which is most videos and a good many
/// photographs.
/// </param>
public sealed record CollectionsResult(
    int Proposed,
    int Grouped,
    int Considered,
    TimeSpan Elapsed,
    bool WasCancelled)
{
    public static CollectionsResult Stopped(TimeSpan elapsed) =>
        new(0, 0, 0, elapsed, WasCancelled: true);

    /// <summary>What a scan says about this phase, or nothing when it found none.</summary>
    public string Summary => Proposed == 0
        ? string.Empty
        : $"{Proposed:N0} albums suggested from {Grouped:N0} photos";
}
