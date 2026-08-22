using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.People;

/// <summary>
/// Works out what a person looked like over each stretch of their life, from
/// the faces the user has confirmed as theirs.
/// </summary>
/// <remarks>
/// Eras are found in the confirmed faces rather than declared as calendar
/// periods. A face model encodes adult bone structure, so across childhood a
/// face changes more than most adults differ from each other - measured on this
/// library, the same child scores 0.88 across one year and 0.56 across eight -
/// while an adult barely moves in a decade. Cutting on the calendar would give a
/// grandparent ten eras that are all the same face and a child too few at the
/// age they change fastest.
/// </remarks>
public static class EraBuilder
{
    /// <summary>
    /// How far a face may drift from the era so far before it belongs to the
    /// next one.
    /// </summary>
    /// <remarks>
    /// Higher than the grouping threshold on purpose. Grouping is deciding
    /// whether two faces are the same person at all; this is deciding whether
    /// the same person still looks the way they did, and it should split sooner
    /// rather than average a face across a change it should have recorded.
    /// </remarks>
    public const float DefaultThreshold = 0.55f;

    /// <summary>
    /// How many faces in a row must disagree before an era is cut.
    /// </summary>
    /// <remarks>
    /// A single face dips below the threshold constantly - a bad angle, a hand
    /// in the way, a dark room - and none of that means anyone has changed.
    /// Cutting on one produced 21 eras from eighteen months of one baby, several
    /// of them a single afternoon, which is noise being recorded as growth. An
    /// era should end when someone stops looking the way they did, and that
    /// shows up in a run of photographs rather than in one.
    /// </remarks>
    public const int ConsecutiveBreaks = 3;

    /// <summary>
    /// How few confirmed faces an era may rest on.
    /// </summary>
    /// <remarks>
    /// An era is an average, and an average of three photographs taken minutes
    /// apart describes that moment rather than that stretch of a life. Anything
    /// smaller is folded into its neighbour.
    /// </remarks>
    public const int MinimumSamples = 12;

    /// <summary>
    /// How short a stretch an era may cover.
    /// </summary>
    /// <remarks>
    /// One party is not a period of someone's life however many photographs were
    /// taken at it, and an era that narrow would match nothing outside the day
    /// it came from.
    /// </remarks>
    public static readonly TimeSpan MinimumSpan = TimeSpan.FromDays(45);

    /// <summary>
    /// Every era the confirmed faces support, earliest first.
    /// </summary>
    /// <remarks>
    /// Only confirmed faces reach here. A proposal the user has not looked at
    /// would otherwise teach the app what it already believes, and a wrong one
    /// would pull the era towards the wrong person and make the next proposal
    /// worse.
    /// </remarks>
    public static IReadOnlyList<PersonEra> Derive(
        IEnumerable<FaceSample> confirmed,
        float threshold = DefaultThreshold,
        int minimumSamples = MinimumSamples)
    {
        ArgumentNullException.ThrowIfNull(confirmed);

        List<FaceSample> ordered =
            [.. confirmed.OrderBy(sample => sample.TakenUtc).ThenBy(sample => sample.FaceId)];

        if (ordered.Count == 0)
        {
            return [];
        }

        var runs = new List<List<FaceSample>>();
        var current = new List<FaceSample>();

        // Faces that have disagreed with the era so far, held rather than acted
        // on. They join it if the next one agrees again, and start the next era
        // only once enough of them have disagreed in a row.
        var dissent = new List<FaceSample>();
        FaceEmbedding centroid = default;

        foreach (FaceSample face in ordered)
        {
            if (current.Count == 0)
            {
                current.Add(face);
                centroid = face.Embedding;
                continue;
            }

            if (centroid.SimilarityTo(face.Embedding) >= threshold)
            {
                current.AddRange(dissent);
                dissent.Clear();
                current.Add(face);
                centroid = FaceEmbedding.Mean([.. current.Select(member => member.Embedding)]);
                continue;
            }

            dissent.Add(face);
            if (dissent.Count >= ConsecutiveBreaks)
            {
                runs.Add(current);
                current = [.. dissent];
                dissent.Clear();
                centroid = FaceEmbedding.Mean([.. current.Select(member => member.Embedding)]);
            }
        }

        current.AddRange(dissent);
        runs.Add(current);

        return [.. Merge(runs, minimumSamples).Select(ToEra)];
    }

    /// <summary>
    /// Folds runs too small or too brief to stand on their own into the run
    /// before them.
    /// </summary>
    private static List<List<FaceSample>> Merge(List<List<FaceSample>> runs, int minimumSamples)
    {
        if (runs.Count <= 1)
        {
            return runs;
        }

        var merged = new List<List<FaceSample>>();
        foreach (List<FaceSample> run in runs)
        {
            if (merged.Count > 0 && !StandsAlone(run, minimumSamples))
            {
                merged[^1].AddRange(run);
                continue;
            }

            merged.Add([.. run]);
        }

        // The first run can be the short one, in which case there was nothing
        // before it to fold into and the fold has to happen forwards instead.
        while (merged.Count > 1 && !StandsAlone(merged[0], minimumSamples))
        {
            merged[1].InsertRange(0, merged[0]);
            merged.RemoveAt(0);
        }

        return merged;
    }

    private static bool StandsAlone(List<FaceSample> run, int minimumSamples) =>
        run.Count >= minimumSamples
        && run[^1].TakenUtc - run[0].TakenUtc >= MinimumSpan;

    private static PersonEra ToEra(List<FaceSample> run) => new()
    {
        // Exclusive at the end, and a single-moment era still has to cover its
        // own faces - hence the tick rather than the bare last date.
        FromUtc = run[0].TakenUtc,
        ToUtc = run[^1].TakenUtc.AddTicks(1),
        Centroid = FaceEmbedding.Mean([.. run.Select(member => member.Embedding)]),
        SampleCount = run.Count,
    };
}
