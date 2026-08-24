namespace PhotoGallery.Application.UseCases.Videos;

/// <summary>What a run of the keyframe pass did.</summary>
/// <param name="Considered">Videos found outstanding when the pass began.</param>
/// <param name="Prepared">Videos that yielded a poster and its companions.</param>
/// <param name="Failed">
/// Videos that were reached and would not decode, now recorded as such so they
/// are not opened again on every future run.
/// </param>
/// <param name="Skipped">
/// Videos that could not be reached at all this time.
/// </param>
/// <remarks>
/// <paramref name="Skipped"/> is deliberately separate from
/// <paramref name="Failed"/>, and the difference is the whole of what stops a
/// blinking share costing a clip its poster for good: nothing is written down
/// for these, and the next run offers them again.
/// </remarks>
public sealed record VideoBuildResult(
    int Considered, int Prepared, int Failed, int Skipped, TimeSpan Elapsed, bool Cancelled)
{
    public string Summary
    {
        get
        {
            if (Considered == 0)
            {
                return "Every video already has a picture on it.";
            }

            string headline = Cancelled
                ? $"Stopped after {Prepared:N0} of {Considered:N0} videos. "
                  + "The rest are still there for next time."
                : $"{Prepared:N0} videos out of {Considered:N0} now have a picture on them, "
                  + $"in {Minutes()}.";

            // Said plainly, and said once. A container Windows has no codec for
            // is not a fault the user can do anything about, and the pass has
            // written that down rather than opening it again every run - so the
            // honest thing is to say it will not be retried instead of leaving
            // somebody waiting for it to sort itself out.
            if (Failed > 0)
            {
                headline += $" {Failed:N0} would not open on this computer "
                            + "and will not be tried again.";
            }

            // The opposite promise, and worth making explicitly. These were not
            // written off, so somebody who reconnects and runs this again gets
            // them - and saying so is what stops the count reading as damage.
            return Skipped == 0
                ? headline
                : headline + $" {Skipped:N0} could not be reached this time "
                           + "and will be tried again.";
        }
    }

    private string Minutes() => Elapsed.TotalMinutes >= 1d
        ? $"{Elapsed.TotalMinutes:N0} min"
        : $"{Elapsed.TotalSeconds:N0}s";
}
