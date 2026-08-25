using PhotoGallery.Application.UseCases.Collections;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Application.UseCases.Videos;

namespace PhotoGallery.Application.UseCases.Refresh;

/// <summary>
/// What one refresh - crawl, generate, place, describe, prepare videos, then
/// find faces - did to the library.
/// </summary>
/// <param name="Generated">
/// Null when the run stopped before generating, so "no pictures were made" and
/// "we never got that far" stay distinguishable.
/// </param>
/// <param name="Described">
/// Null only when the run stopped before describing. Missing search models are
/// <em>not</em> this case: the describing pass answers with a result that says
/// so, which is a run that happened and found nothing it could do rather than
/// one that never started.
/// </param>
/// <param name="Located">
/// Null when the run stopped before working out where the photographs were
/// taken. An unreachable source is <em>not</em> this case: the pass names it in
/// a result it did return.
/// </param>
/// <param name="Videos">
/// Null when the run stopped before reaching the videos - so on the same
/// principle, "no video needed a picture" and "we never got that far" stay
/// distinguishable.
/// </param>
/// <param name="Faces">
/// Null when the run stopped before the last phase. Missing face models are not
/// this case, for the same reason missing search models are not this case for
/// <paramref name="Described"/>.
/// </param>
public sealed record RefreshResult(
    IReadOnlyList<ScanResult> Scans,
    ThumbnailBuildResult? Generated,
    ContentIndexResult? Described,
    LocatePhotosResult? Located,
    VideoBuildResult? Videos,
    FaceDetectionResult? Faces,
    CollectionsResult? Collected,
    TimeSpan Elapsed,
    bool WasCancelled)
{
    public int Added => Scans.Sum(scan => scan.Added);

    public int Updated => Scans.Sum(scan => scan.Updated);

    public int Removed => Scans.Sum(scan => scan.Removed);

    public int Built => Generated?.Built ?? 0;

    /// <summary>How many videos were given a picture this run.</summary>
    public int VideosPrepared => Videos?.Prepared ?? 0;

    /// <summary>How many faces were found this run.</summary>
    public int FacesFound => Faces?.FacesFound ?? 0;

    /// <summary>How many occasions are on offer after this run.</summary>
    public int CollectionsProposed => Collected?.Proposed ?? 0;

    /// <summary>How many photographs were given the name of a place this run.</summary>
    public int PhotosPlaced => Located?.Named ?? 0;

    /// <summary>How many pictures became searchable by description.</summary>
    public int NowSearchable =>
        Described is { ModelsMissing: false } content ? content.Described : 0;

    /// <summary>Sources that could not be read, so nothing about them was changed.</summary>
    public int Unavailable => Scans.Count(scan => scan.WasUnavailable);

    /// <summary>
    /// Whether the grid and the folder tree need re-reading.
    /// </summary>
    /// <remarks>
    /// Only what changes the tiles counts. Faces and places are written by the
    /// scan too, but neither adds a tile or a folder - and this flag is what
    /// makes the caller re-query the whole library, so including them meant a
    /// full reload of sixteen thousand tiles after every routine rescan that
    /// happened to find one face. People has its own refresh for its own screen.
    /// </remarks>
    public bool ChangedAnything =>
        Scans.Any(scan => scan.ChangedAnything) || Built > 0 || VideosPrepared > 0;

    /// <summary>
    /// The optional parts that were not installed, named as the user knows them.
    /// </summary>
    /// <remarks>
    /// Only where the phase actually reached the question. A run stopped before
    /// it got that far has a null result and reports nothing, which is right: it
    /// does not know.
    /// </remarks>
    private List<string> Skipped()
    {
        var missing = new List<string>(2);

        if (Described is { ModelsMissing: true })
        {
            missing.Add("the search model");
        }

        if (Faces is { ModelsMissing: true })
        {
            missing.Add("the face model");
        }

        return missing;
    }

    public string Summary
    {
        get
        {
            // Only mentioned when it did something. A library whose search
            // models are not installed should not be told about a step it
            // cannot run.
            string searchable = NowSearchable > 0
                ? $", {NowSearchable:N0} described"
                : string.Empty;

            // Same rule throughout: a phase is mentioned only where it did
            // something. A library of photographs should read nothing about
            // videos, and one whose faces were all found long ago should read
            // nothing about faces.
            string placed = PhotosPlaced > 0 ? $", {PhotosPlaced:N0} placed" : string.Empty;

            string videos = VideosPrepared > 0
                ? $", {VideosPrepared:N0} videos prepared"
                : string.Empty;

            string faces = FacesFound > 0 ? $", {FacesFound:N0} faces found" : string.Empty;

            string counted = $"{Added:N0} new, {Updated:N0} changed, {Removed:N0} gone, "
                           + $"{Built:N0} pictures prepared{searchable}{placed}{videos}{faces} "
                           + $"({Elapsed.TotalSeconds:N1}s)";

            if (WasCancelled)
            {
                return $"stopped - {counted}. What was not reached is still pending, "
                     + "so running this again carries on from here";
            }

            // Which parts could not run at all. Said because these phases are no
            // longer buttons somebody chose to press: a scan that quietly did
            // five things out of six, with the sixth needing a model that is not
            // installed, would look like a scan that had done everything.
            string skipped = Skipped() switch
            {
                [] => string.Empty,
                [string only] => $". {only} is not installed, so that part was skipped",
                var many => $". {string.Join(" and ", many)} are not installed, so those "
                            + "parts were skipped",
            };

            if (Unavailable == 0)
            {
                return counted + skipped;
            }

            return Unavailable == 1
                ? $"{counted}{skipped}. One folder could not be reached and was left untouched"
                : $"{counted}{skipped}. {Unavailable:N0} folders could not be reached and were "
                + "left untouched";
        }
    }
}
