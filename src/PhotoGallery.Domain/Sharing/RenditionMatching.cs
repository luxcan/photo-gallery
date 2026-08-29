namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Which of another machine's prepared photographs this one can take, and which
/// it must prepare itself.
/// </summary>
/// <remarks>
/// The arithmetic that makes the pool worth building: on this library, roughly
/// an hour of reading 24.8 GB replaced by about five minutes of copying, and the
/// photographs themselves never opened.
///
/// <para><strong>Two rules keep it safe, and both are here rather than in the
/// copying.</strong> A picture is only taken when the file agrees byte for byte
/// and to the second; and a rendition is only ever accepted for a row that
/// already exists. Pure, so both can be argued out against tests rather than
/// against two real thumbnail stores.</para>
/// </remarks>
public static class RenditionMatching
{
    /// <summary>
    /// What this library can fill in from what the others have prepared.
    /// </summary>
    /// <param name="here">
    /// Photographs this library has indexed and not prepared. Rows it has
    /// already prepared are not offered a second picture: the work is done, and
    /// replacing it would be a fetch that bought nothing.
    /// </param>
    /// <param name="theirs">Every other machine's manifest.</param>
    /// <param name="held">
    /// Rendition names this library already has on disk. What is left is what
    /// has to be fetched, which is what makes running the exchange twice copy
    /// nothing the second time.
    /// </param>
    public static PoolPlan Match(
        IReadOnlyList<Unprepared> here,
        IReadOnlyList<PreparedSet> theirs,
        IReadOnlyCollection<string> held)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(theirs);
        ArgumentNullException.ThrowIfNull(held);

        if (here.Count == 0 || theirs.Count == 0)
        {
            return PoolPlan.Nothing;
        }

        Dictionary<AssetKey, PreparedFact> offered = Offered(theirs);

        List<PreparedFact> fillIn = [];
        HashSet<string> wanted = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> already = new(held, StringComparer.OrdinalIgnoreCase);
        int mismatched = 0;

        foreach (Unprepared photo in here)
        {
            if (!offered.TryGetValue(photo.Photo, out PreparedFact? fact))
            {
                continue;
            }

            // The path says which photograph; the bytes say which picture. A
            // copy re-saved, cropped or re-encoded is a different file wearing
            // the same name, and taking its rendition would put the wrong image
            // on screen with nothing to say so.
            if (!fact.Describes(photo.Length, photo.ModifiedUtc))
            {
                mismatched++;
                continue;
            }

            fillIn.Add(fact);

            // A file that never decoded brings its status and nothing else,
            // which is the point: twelve files that will never decode should not
            // be read again on four more machines.
            if (fact.ThumbnailName is string name && !already.Contains(name))
            {
                wanted.Add(name);
            }
        }

        return new PoolPlan(fillIn, wanted, mismatched);
    }

    /// <summary>
    /// The rendition names this library should offer, and the ones it must not.
    /// </summary>
    /// <remarks>
    /// <strong>Only pictures nobody has turned.</strong> A turn rewrites both
    /// files in place, under a name derived from the original's bytes - which
    /// the turn does not change. So a straightened photograph here and the same
    /// photograph elsewhere are one name over two different pictures, and "take
    /// the names I do not have" would hand somebody a sideways tile at random.
    ///
    /// <para>Fetching stays unconditional, and that asymmetry is deliberate: a
    /// machine that has already merged a turn still needs the as-generated
    /// rendition, because it is the only one the pool holds, and turns it itself
    /// once it has it. Forbidding the fetch too would leave exactly the
    /// photographs somebody cared enough to straighten falling back to an hour
    /// of reading originals.</para>
    /// </remarks>
    /// <param name="mine">Every rendition this library holds, with its rotation.</param>
    /// <param name="pooled">What the pool already has.</param>
    public static IReadOnlyCollection<string> Offerable(
        IReadOnlyList<PooledRendition> mine, IReadOnlyCollection<string> pooled)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(pooled);

        HashSet<string> there = new(pooled, StringComparer.OrdinalIgnoreCase);
        HashSet<string> turned = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> offer = new(StringComparer.OrdinalIgnoreCase);

        // Gathered in two passes rather than one, because several rows share a
        // rendition and one of them being turned makes that picture unfit for
        // everybody. A single pass would offer it or not depending on which
        // duplicate came first.
        foreach (PooledRendition rendition in mine.Where(r => r.Rotation != 0))
        {
            turned.Add(rendition.Name);
        }

        foreach (PooledRendition rendition in mine)
        {
            if (!turned.Contains(rendition.Name) && !there.Contains(rendition.Name))
            {
                offer.Add(rendition.Name);
            }
        }

        return offer;
    }

    /// <summary>
    /// The latest fact about each photograph, across every machine.
    /// </summary>
    /// <remarks>
    /// Two machines can have prepared the same photograph, and where their
    /// answers differ the file has changed under one of them. The later manifest
    /// wins for the same reason a later decision does - and the byte check
    /// afterwards means a wrong guess here costs a local prepare rather than a
    /// wrong picture.
    /// </remarks>
    private static Dictionary<AssetKey, PreparedFact> Offered(IReadOnlyList<PreparedSet> theirs)
    {
        Dictionary<AssetKey, PreparedFact> offered = [];
        Dictionary<AssetKey, DateTime> when = [];

        foreach (PreparedSet them in theirs)
        {
            foreach (PreparedFact fact in them.Facts)
            {
                if (!when.TryGetValue(fact.Photo, out DateTime standing)
                    || them.WrittenUtc > standing)
                {
                    offered[fact.Photo] = fact;
                    when[fact.Photo] = them.WrittenUtc;
                }
            }
        }

        return offered;
    }
}

/// <summary>One of this library's cached pictures, and whether anybody has turned it.</summary>
public sealed record PooledRendition(string Name, int Rotation);
