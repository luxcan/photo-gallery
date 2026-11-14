using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Application.UseCases.Videos;

namespace PhotoGallery.Application.UseCases.Refresh;

/// <summary>
/// What one refresh - crawl, take from the sync folder, generate, place,
/// describe, prepare videos, find faces, then give to the sync folder - did to
/// the library.
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
/// <param name="Answers">
/// Null when the run stopped before applying what another machine had decided
/// about photographs this library had not indexed. A library nobody shares with
/// has nothing waiting and gets a result saying so, which is a phase that ran
/// and found no work rather than one that never started.
/// </param>
/// <param name="Taken">
/// Null when the run stopped before taking anything from the sync folder. A
/// library with no sync folder, or one that did not answer in time, is not this
/// case: the phase ran, found it could not use the folder, and says so.
/// </param>
/// <param name="Given">
/// Null when the run stopped before giving - and when the take found the sync
/// folder could not be used, because the same scan asking again would only say
/// the same thing a second time.
/// </param>
public sealed record RefreshResult(
    IReadOnlyList<ScanResult> Scans,
    ThumbnailBuildResult? Generated,
    ContentIndexResult? Described,
    LocatePhotosResult? Located,
    VideoBuildResult? Videos,
    FaceDetectionResult? Faces,
    HeldResult? Answers,
    AlbumsResult? Collected,
    PoolResult? Taken,
    PoolResult? Given,
    TimeSpan Elapsed,
    bool WasCancelled)
{
    public int Added => Scans.Sum(scan => scan.Added);

    public int Updated => Scans.Sum(scan => scan.Updated);

    public int Removed => Scans.Sum(scan => scan.Removed);

    public int Built => Generated?.Built ?? 0;

    /// <summary>
    /// How many photographs were filled in from the small copies another
    /// computer left, rather than made here.
    /// </summary>
    public int PicturesTaken => Taken?.Filled ?? 0;

    /// <summary>How many small copies were left for the other computers.</summary>
    public int PicturesGiven => Given?.Offered ?? 0;

    /// <summary>How many faces another computer found were taken this run.</summary>
    public int FacesTaken => Taken?.Faces ?? 0;

    /// <summary>How many videos were given a picture this run.</summary>
    public int VideosPrepared => Videos?.Prepared ?? 0;

    /// <summary>How many faces were found this run.</summary>
    public int FacesFound => Faces?.FacesFound ?? 0;

    /// <summary>How many answers from another machine were written this run.</summary>
    /// <remarks>
    /// Both sweeps: the scan's own, and the one taking the small copies runs
    /// when a picture or a face it brought is what an answer was waiting for.
    /// </remarks>
    public int AnswersApplied => (Answers?.Applied ?? 0) + (Taken?.Swept?.Applied ?? 0);

    /// <summary>
    /// How many waiting answers were let go this run without writing anything,
    /// because what stands here already settles them.
    /// </summary>
    public int AnswersSettled => (Answers?.Settled ?? 0) + (Taken?.Swept?.Settled ?? 0);

    /// <summary>
    /// How many answers are still waiting after this run, for whatever reason.
    /// </summary>
    /// <remarks>
    /// The one number here that is not about what just happened. Most of these
    /// are about pictures that are only on somebody else's laptop and they come
    /// good the day those reach the shared folder - but a count that is never
    /// said is a count that grows quietly, and this is the difference between
    /// "nothing to do" and "an evening's work is waiting for a folder nobody has
    /// added".
    ///
    /// <para>One number for every reason an answer waits, including a move into
    /// an album this library does not have. Why each one waits is the Sync
    /// screen's to say, split by what would have to happen; a line in a scan log
    /// that named one reason for all of them was false for most.</para>
    ///
    /// <para>From the last sweep that ran, because each one counts everything
    /// still waiting rather than what it added: a scan stopped after taking the
    /// small copies has only that sweep's word for it.</para>
    /// </remarks>
    public int AnswersWaiting =>
        (Answers ?? Taken?.Swept) is HeldResult last ? last.Waiting + last.Unplaced : 0;

    /// <summary>How many occasions are on offer after this run.</summary>
    public int AlbumsProposed => Collected?.Proposed ?? 0;

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
    ///
    /// <para>A photograph filled in from another computer's small copy is a
    /// tile as much as one made here, so it counts.</para>
    /// </remarks>
    public bool ChangedAnything =>
        Scans.Any(scan => scan.ChangedAnything)
        || Built > 0
        || VideosPrepared > 0
        || PicturesTaken > 0;

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

    /// <summary>
    /// One line for each phase that ran, in the order they ran, saying how long
    /// it took and what it did.
    /// </summary>
    /// <remarks>
    /// Every phase has always measured itself - each of these results carries its
    /// own Elapsed and its own Summary - and this record threw all eight away and
    /// printed one total. So a scan that took thirty-seven minutes said only that
    /// it took thirty-seven minutes, and whether they went on the share, the
    /// processor or the video decoder was a guess. Nothing could be measured
    /// before and after a change, which is the same as saying nothing could be
    /// optimised on purpose.
    ///
    /// <para>A phase that never ran is absent rather than zero: "we never got
    /// that far" and "there was nothing to do" are the distinction the whole
    /// record is built around, and a line of zeroes would lose it. What the
    /// named phases do not account for is reported as one remainder, so the
    /// lines always add up to the total.</para>
    /// </remarks>
    public IReadOnlyList<string> Timings
    {
        get
        {
            List<string> lines = [];
            TimeSpan accounted = TimeSpan.Zero;

            foreach (ScanResult scan in Scans)
            {
                lines.Add(Line("crawl", scan.Elapsed, scan.Summary));
                accounted += scan.Elapsed;
            }

            // The sync folder's two phases say what they did, and also why they
            // did nothing: a folder not chosen, asleep or away is one line here
            // rather than a failed scan, and the only place it is said at all.
            Add("taking", Taken?.Elapsed, Taken?.Summary);
            Add("pictures", Generated?.Elapsed, Generated?.Summary);
            Add("places", Located?.Elapsed, Located?.Summary);
            Add("describing", Described?.Elapsed, Described?.Summary);
            Add("videos", Videos?.Elapsed, Videos?.Summary);
            Add("faces", Faces?.Elapsed, Faces?.Summary);
            Add("albums", Collected?.Elapsed, Collected?.Summary);
            Add("giving", Given?.Elapsed, Given?.Summary);

            // Whatever the named phases do not account for. Applying held
            // answers is the one phase that has never timed itself, and giving
            // it a stopwatch would mean changing a sharing record and the test
            // that compares one by value - for a phase nobody is optimising. A
            // remainder costs nothing and is strictly better than timing each
            // one, because it also catches what no phase owns: opening the
            // scope, the reconciliation between passes, the final counts. The
            // lines add up to the total, and that is the property worth having.
            TimeSpan other = Elapsed - accounted;
            if (other > TimeSpan.FromSeconds(0.5))
            {
                lines.Add(Line("other", other, "everything the phases above do not own"));
            }

            lines.Add(Line("total", Elapsed, Summary));

            return lines;

            void Add(string name, TimeSpan? elapsed, string? summary)
            {
                if (elapsed is not null)
                {
                    lines.Add(Line(name, elapsed.Value, summary ?? string.Empty));
                    accounted += elapsed.Value;
                }
            }
        }
    }

    /// <summary>A phase, how long it took, and what it did.</summary>
    private static string Line(string name, TimeSpan elapsed, string did) =>
        $"{name,-11}{Took(elapsed),9}  {did}";

    /// <summary>A count of answers, with one said as one.</summary>
    private static string AnswerCount(int count) =>
        count == 1 ? "1 change" : $"{count:N0} changes";

    /// <summary>
    /// A duration at the precision somebody reading a scan log cares about.
    /// </summary>
    /// <remarks>
    /// Tenths below a minute, because the crawl and the album pass are measured
    /// in seconds and the difference between 3.1 and 3.9 is worth seeing. Whole
    /// minutes above it, because the long phases run for tens of minutes and the
    /// seconds are noise once they do.
    /// </remarks>
    private static string Took(TimeSpan elapsed) => elapsed.TotalMinutes switch
    {
        < 1 => $"{elapsed.TotalSeconds:N1}s",
        < 60 => $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s",
        _ => $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m",
    };

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

            // What the other computers saved this one, and what this one left
            // for them - each only where it happened, like everything here.
            string taken = PicturesTaken > 0
                ? $", {PicturesTaken:N0} taken from another computer"
                : string.Empty;

            string facesTaken = FacesTaken > 0
                ? $", {FacesTaken:N0} faces taken from another computer"
                : string.Empty;

            string given = PicturesGiven > 0
                ? $", {PicturesGiven:N0} left for the other computers"
                : string.Empty;

            // Both sides of the sharing phase, and only where there is one. A
            // library nobody shares with reads nothing about answers at all.
            //
            // Applied and settled apart, because only one of them is another
            // machine's work arriving. A settled answer is let go without a
            // write - and a single number for both said "applied" about answers
            // that changed nothing.
            //
            // The settled words name no reason. An answer is settled because it
            // was already true here, because something here beat it, or because
            // the album or the person it named was deleted here, and "settled by
            // later answers" was false for most of them: often the waiting answer
            // was itself the later one, or the same answer arriving twice.
            string applied = AnswersApplied > 0
                ? $", {AnswerCount(AnswersApplied)} applied"
                : string.Empty;

            string settled = AnswersSettled > 0
                ? $", {AnswerCount(AnswersSettled)} made no difference"
                : string.Empty;

            string counted = $"{Added:N0} new, {Updated:N0} changed, {Removed:N0} gone, "
                           + $"{Built:N0} pictures prepared{taken}{searchable}{placed}{videos}"
                           + $"{faces}{facesTaken}{applied}{settled}{given} "
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

            // Last, and on its own, because it is the only part of this that
            // may ask for something. It names no reason: the answers wait on a
            // photograph, a face, a small copy or an album, and one sentence
            // naming one of those was false for most of them on a real library.
            // The Sync screen says which, split by what would have to happen.
            string waiting = AnswersWaiting switch
            {
                0 => string.Empty,
                1 => ". 1 change is still waiting for something this library does not have "
                     + "yet - the Sync screen says what",
                _ => $". {AnswersWaiting:N0} changes are still waiting for something this "
                     + "library does not have yet - the Sync screen says what",
            };

            if (Unavailable == 0)
            {
                return counted + skipped + waiting;
            }

            return Unavailable == 1
                ? $"{counted}{skipped}. One folder could not be reached and was left "
                + $"untouched{waiting}"
                : $"{counted}{skipped}. {Unavailable:N0} folders could not be reached and were "
                + $"left untouched{waiting}";
        }
    }
}
