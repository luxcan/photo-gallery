namespace PhotoGallery.Application.Ports;

/// <summary>Headline totals, cheap enough to show in the status bar.</summary>
/// <param name="VideosPrepared">
/// How many videos have a picture the library can draw. Counted from the rows
/// rather than by looking on disk, because the question being asked is what the
/// grid can show - and because a stat per video belongs to the pass that makes
/// them, not to a total that is re-read every time anything changes.
/// </param>
/// <param name="VideosUnreadable">
/// How many videos were reached, would not decode, and have been recorded as
/// such. The pass never offers these again, so they are neither prepared nor
/// waiting - and counting them as waiting, which is what the row alone would
/// say, leaves the screen promising a rescan that will never come.
/// </param>
public sealed record LibraryCounts(
    int Photos,
    int Videos,
    int VideosPrepared,
    int VideosUnreadable,
    int Thumbnails,
    int Faces,
    int People,
    int UnresolvedDuplicateSets)
{
    public static LibraryCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    public int TotalAssets => Photos + Videos;

    /// <summary>
    /// Videos that are in the index, have no picture on them yet, and will be
    /// offered to the next scan.
    /// </summary>
    public int VideosWaiting => Videos - VideosPrepared - VideosUnreadable;
}
