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
/// <param name="AwaitingFaces">
/// How many pictures a face pass has never looked at. The reason it is counted
/// rather than inferred from <paramref name="Faces"/> being nought: a library of
/// landscapes can have been scanned properly and hold no faces at all, and a
/// screen that read that as "not looked at yet" would say so for ever.
/// </param>
/// <param name="Collections">
/// How many occasions the library holds, suggested and made together. A size
/// rather than a to-do: the number of suggestions still unanswered belongs on
/// the tab that shows them, and one number cannot mean two things.
/// </param>
/// <param name="AwaitingDescription">
/// The same question for the describing pass. Both exist because the models are
/// downloaded after a library has been scanned, so the ordinary first install
/// leaves both passes with everything still to do and nothing on screen said so.
/// </param>
public sealed record LibraryCounts(
    int Photos,
    int Videos,
    int VideosPrepared,
    int VideosUnreadable,
    int Thumbnails,
    int Faces,
    int People,
    int UnresolvedDuplicateSets,
    int AwaitingFaces = 0,
    int AwaitingDescription = 0,
    int Collections = 0)
{
    public static LibraryCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    public int TotalAssets => Photos + Videos;

    /// <summary>
    /// Videos that are in the index, have no picture on them yet, and will be
    /// offered to the next scan.
    /// </summary>
    public int VideosWaiting => Videos - VideosPrepared - VideosUnreadable;
}
