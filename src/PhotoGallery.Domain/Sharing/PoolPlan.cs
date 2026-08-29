namespace PhotoGallery.Domain.Sharing;

/// <summary>A photograph this library has indexed but not yet prepared.</summary>
/// <remarks>
/// The length and the moment come from the crawl, which collected them for free.
/// They are what decides whether a pooled picture is a picture of these bytes.
/// </remarks>
public sealed record Unprepared(AssetKey Photo, long Length, DateTime ModifiedUtc);

/// <summary>
/// What taking the pool would do to this library.
/// </summary>
/// <param name="FillIn">
/// Facts to write onto rows this library already has, each with the two
/// renditions to fetch for it.
/// </param>
/// <param name="Wanted">
/// The rendition names to fetch, each of which is a tile and a preview. Fewer
/// than <paramref name="FillIn"/> where several rows share one picture, which
/// duplicates do.
/// </param>
/// <param name="Mismatched">
/// Photographs another machine has prepared at the same path whose bytes differ
/// from the file here. Counted rather than hidden: it is the number that
/// separates "the pool had nothing for me" from "the two of you are looking at
/// different files".
/// </param>
public sealed record PoolPlan(
    IReadOnlyList<PreparedFact> FillIn,
    IReadOnlyCollection<string> Wanted,
    int Mismatched)
{
    public static PoolPlan Nothing { get; } = new([], new HashSet<string>(), 0);

    public bool ChangesNothing => FillIn.Count == 0;
}
