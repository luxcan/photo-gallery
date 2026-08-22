namespace PhotoGallery.Application.UseCases.Refresh;

/// <summary>Which part of a refresh is running.</summary>
public enum RefreshPhase
{
    /// <summary>Crawling the folders and reconciling what is there against the index.</summary>
    Indexing = 0,

    /// <summary>Making the renditions the crawl found were missing.</summary>
    Generating = 1,

    /// <summary>Reading each photograph's coordinates and naming the place.</summary>
    /// <remarks>
    /// First of the long phases, because it is the only one that needs the
    /// sources: the crawl has just proved they are reachable, and doing the
    /// network-bound work at the front means a stop later costs none of it.
    /// </remarks>
    Locating = 2,

    /// <summary>
    /// Working out what the new pictures are of, so they can be found by
    /// describing them.
    /// </summary>
    Describing = 3,

    /// <summary>
    /// Taking a picture out of each video the crawl found, so the videos show up
    /// in the library the way photographs do.
    /// </summary>
    /// <remarks>
    /// Late, and deliberately: this is the phase measured in hours, so it is the
    /// one somebody actually stops, and everything before it is already written
    /// by the time it starts. Put it earlier and a stopped scan would leave the
    /// library never described and never placed.
    /// </remarks>
    PreparingVideos = 4,

    /// <summary>
    /// Looking for faces in everything that now has a picture to look at.
    /// </summary>
    /// <remarks>
    /// After the videos rather than before them, because it reads what that
    /// phase writes: a clip's keyframes are looked at for faces exactly as a
    /// photograph's preview is. Run before, and every face in every video would
    /// wait for the following scan.
    /// </remarks>
    FindingFaces = 5,
}

/// <summary>Progress of a refresh, reported as it goes.</summary>
/// <param name="Target">The folder being crawled, or empty once generating.</param>
/// <param name="Remaining">
/// Roughly how much longer this phase has, where the phase can tell. Placing,
/// describing and preparing videos do, and they are the ones that need it: an
/// hour-long bar with no end in sight is what makes somebody stop a pass that
/// was nearly finished.
/// </param>
public readonly record struct RefreshProgress(
    RefreshPhase Phase, string Target, int Done, int Total, int Failed,
    TimeSpan? Remaining = null)
{
    /// <summary>
    /// True while crawling, because a crawl cannot know how many files it will
    /// find until it has finished finding them. The count is the honest signal
    /// then; the bar only says that something is happening.
    /// </summary>
    /// <remarks>
    /// True for the phases that open by working out how much there is to do, and
    /// only until they have: the video phase sweeps the disk for missing posters,
    /// the face and place phases each begin with a query, and none of them
    /// reports anything while that runs. A bar sitting at zero through it would
    /// say the pass had stalled. Deliberately not widened to every phase with no
    /// total - generating is left as it is rather than changed in passing.
    /// </remarks>
    public bool IsIndeterminate =>
        Phase == RefreshPhase.Indexing
        || (Total == 0 && Phase is RefreshPhase.PreparingVideos
                              or RefreshPhase.FindingFaces
                              or RefreshPhase.Locating);

    public double Fraction => Total == 0 ? 0d : (double)Done / Total;
}
