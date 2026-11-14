using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.Duplicates;

/// <summary>
/// Suggests which copy of a picture is worth keeping, or says nothing separates
/// them (PRP 14).
/// </summary>
/// <remarks>
/// Copies are compared one rung at a time - eyes, sharpness, exposure, original
/// over re-saved, pixels - and a rung only speaks when the difference is
/// clear: every copy within its margin of the best goes on to the next rung.
/// When one copy is left standing it is suggested and the rung that left it is
/// named; when the last rung still leaves several, nothing is suggested.
///
/// <para>There is no folder rule. Which folder a copy sits in says nothing about
/// the picture, and copies are handed over without their paths so nothing here
/// could fall back on one.</para>
///
/// <para>Pure, and holding every margin, so the whole decision is testable
/// without a model or a picture. Each margin was set from the phase 1
/// measurement of the real library; the numbers behind them are in the
/// PRP.</para>
/// </remarks>
public static class KeeperPolicy
{
    /// <summary>
    /// How many times sharper one copy must be than another before sharpness
    /// speaks, with every preview measured at the common size.
    /// </summary>
    /// <remarks>
    /// At the common size the plainly shaken or soft frames lose by 1.61 to
    /// 2.18 times, while the same frame at two sizes differs by at most 1.26
    /// and a messaging-app re-save by at most 1.53. This sits between the last
    /// two numbers that must not cross.
    /// </remarks>
    public const double SharpnessMargin = 1.57;

    /// <summary>
    /// How much more of one copy may be blown white, or crushed black, than the
    /// best copy before exposure speaks.
    /// </summary>
    /// <remarks>
    /// Plainly too bright or too dark frames sit 0.053 to 0.149 above the best;
    /// brighter but not plainly wrong ones 0.038 to 0.044, and the same frame at
    /// two sizes at most 0.031. The gap above the margin is narrow.
    /// </remarks>
    public const double ExposureMargin = 0.050;

    /// <summary>The suggestion for one set of copies, with a verdict per copy.</summary>
    /// <param name="copies">The copies still in the set, in any order.</param>
    /// <param name="readEyes">
    /// False when the eye model is not installed, which turns the eyes rung off
    /// entirely - eye states measured before it went are not trusted either.
    /// </param>
    /// <exception cref="ArgumentException">The set is empty.</exception>
    public static KeeperChoice Choose(IReadOnlyList<CopyQuality> copies, bool readEyes = true)
    {
        ArgumentNullException.ThrowIfNull(copies);
        if (copies.Count == 0)
        {
            throw new ArgumentException("Cannot choose a keeper from an empty set.", nameof(copies));
        }

        var lostOn = new Dictionary<int, KeeperCriterion>();
        var exposure = new Dictionary<int, ExposureFault>();
        Dictionary<int, IReadOnlyList<CopyFace>> closed = readEyes
            ? copies.ToDictionary(copy => copy.AssetId, copy => PairedClosed(copy, copies))
            : copies.ToDictionary(copy => copy.AssetId, _ => (IReadOnlyList<CopyFace>)[]);

        List<CopyQuality> running = [.. copies];
        KeeperCriterion? decidedBy = null;

        foreach (KeeperCriterion rung in Enum.GetValues<KeeperCriterion>())
        {
            if (running.Count == 1)
            {
                break;
            }

            List<CopyQuality> left = rung switch
            {
                KeeperCriterion.Eyes => readEyes ? FewestClosed(running, closed) : running,
                KeeperCriterion.Sharpness => Sharpest(running),
                KeeperCriterion.Exposure => BestExposed(running, exposure),
                KeeperCriterion.Original => Originals(running),
                KeeperCriterion.Pixels => Largest(running),
                _ => running,
            };

            foreach (CopyQuality beaten in running.Except(left))
            {
                lostOn[beaten.AssetId] = rung;
            }

            if (left.Count == 1 && running.Count > 1)
            {
                decidedBy = rung;
            }

            running = left;
        }

        int? keeper = running.Count == 1 ? running[0].AssetId : null;

        return new KeeperChoice(
            keeper,
            keeper is null ? null : decidedBy,
            [
                .. copies.Select(copy => new CopyVerdict(
                    copy.AssetId,
                    lostOn.TryGetValue(copy.AssetId, out KeeperCriterion rung) ? rung : null,
                    exposure.GetValueOrDefault(copy.AssetId),
                    LostOnEyes(lostOn, copy) ? closed[copy.AssetId] : [])),
            ]);
    }

    /// <summary>
    /// The known faces closed in this copy whose person reads Open in another
    /// copy of the set.
    /// </summary>
    /// <remarks>
    /// Paired, because Closed on its own is not a difference: a lowered gaze or
    /// a squint reads Closed in one frame and Unknown in the next, and counting
    /// those suggested a plainly shaken frame in phase 1. Closed against
    /// Unknown, or closed in every copy, says nothing.
    /// </remarks>
    private static IReadOnlyList<CopyFace> PairedClosed(CopyQuality copy, IReadOnlyList<CopyQuality> set) =>
    [
        .. copy.KnownFaces.Where(face =>
            face.Eyes == EyeState.Closed
            && set.Any(other => other.AssetId != copy.AssetId
                                && other.KnownFaces.Any(seen =>
                                    seen.PersonId == face.PersonId && seen.Eyes == EyeState.Open))),
    ];

    /// <summary>
    /// Whether this copy dropped out on the eyes rung - the only copies whose
    /// closed faces are a reason, and so the only ones marked for them.
    /// </summary>
    private static bool LostOnEyes(IReadOnlyDictionary<int, KeeperCriterion> lostOn, CopyQuality copy) =>
        lostOn.TryGetValue(copy.AssetId, out KeeperCriterion rung) && rung == KeeperCriterion.Eyes;

    private static List<CopyQuality> FewestClosed(
        List<CopyQuality> running, IReadOnlyDictionary<int, IReadOnlyList<CopyFace>> closed)
    {
        int fewest = running.Min(copy => closed[copy.AssetId].Count);
        return [.. running.Where(copy => closed[copy.AssetId].Count == fewest)];
    }

    /// <summary>
    /// The copies not clearly less sharp than the sharpest.
    /// </summary>
    /// <remarks>
    /// Read over the faces of the known people every copy still in the running
    /// shows, because a sharp background behind a blurred child is not a sharp
    /// photograph - and only those people, because a copy holding one more,
    /// smaller face would otherwise lose for it. Without such a person, over
    /// the most detailed quarter of the scene. A copy with no reading stays in:
    /// a preview smaller than the common size cannot be compared at it, and a
    /// re-saved smaller copy must never lose on sharpness. That holds for a copy
    /// whose faces were not measured too - it goes on, and the faces still
    /// decide between the copies that have them, as long as two do.
    /// </remarks>
    private static List<CopyQuality> Sharpest(List<CopyQuality> running)
    {
        HashSet<int> common = [.. running[0].KnownFaces.Select(face => face.PersonId)];
        foreach (CopyQuality copy in running.Skip(1))
        {
            common.IntersectWith(copy.KnownFaces.Select(face => face.PersonId));
        }

        Func<CopyQuality, double?> score =
            common.Count > 0 && running.Count(copy => FaceSharpness(copy, common) is not null) >= 2
                ? copy => FaceSharpness(copy, common)
                : copy => copy.Sharpness;

        List<double> readings = [.. running.Select(score).OfType<double>()];
        if (readings.Count < 2)
        {
            return running;
        }

        double best = readings.Max();
        return
        [
            .. running.Where(copy => score(copy) is not double reading
                                     || reading * SharpnessMargin >= best),
        ];
    }

    /// <summary>
    /// The common people's faces pooled by area, or null when any of them was
    /// not measured.
    /// </summary>
    private static double? FaceSharpness(CopyQuality copy, IReadOnlySet<int> people)
    {
        double weighted = 0d, area = 0d;
        foreach (CopyFace face in copy.KnownFaces.Where(face => people.Contains(face.PersonId)))
        {
            if (face.Sharpness is not double sharpness)
            {
                return null;
            }

            weighted += sharpness * face.Bounds.Area;
            area += face.Bounds.Area;
        }

        return area > 0d ? weighted / area : null;
    }

    /// <summary>
    /// The copies not clearly more blown out or crushed than the best.
    /// </summary>
    /// <remarks>
    /// Each side is weighed against the lowest share in the running. Two copies
    /// each wrong a different way would both be thrown out by that, and a rung
    /// that removes every copy has said nothing, so then it ties.
    /// </remarks>
    private static List<CopyQuality> BestExposed(
        List<CopyQuality> running, Dictionary<int, ExposureFault> faults)
    {
        List<CopyQuality> measured = [.. running.Where(copy => copy is { ClippedHigh: not null, ClippedLow: not null })];
        if (measured.Count < 2)
        {
            return running;
        }

        double brightest = measured.Min(copy => copy.ClippedHigh!.Value) + ExposureMargin;
        double darkest = measured.Min(copy => copy.ClippedLow!.Value) + ExposureMargin;

        var found = new Dictionary<int, ExposureFault>();
        foreach (CopyQuality copy in measured)
        {
            if (copy.ClippedHigh!.Value > brightest)
            {
                found[copy.AssetId] = ExposureFault.TooBright;
            }
            else if (copy.ClippedLow!.Value > darkest)
            {
                found[copy.AssetId] = ExposureFault.TooDark;
            }
        }

        List<CopyQuality> left = [.. running.Where(copy => !found.ContainsKey(copy.AssetId))];
        if (left.Count == 0)
        {
            return running;
        }

        foreach ((int assetId, ExposureFault fault) in found)
        {
            faults[assetId] = fault;
        }

        return left;
    }

    /// <summary>
    /// The copies that still carry a capture date or coordinates, when any do.
    /// </summary>
    /// <remarks>
    /// Whether, not how many. Phase 1 found two sets where the copy with
    /// coordinates was the smaller one, so "more details" is not a safe signal.
    /// </remarks>
    private static List<CopyQuality> Originals(List<CopyQuality> running)
    {
        List<CopyQuality> originals = [.. running.Where(copy => copy.CarriesCameraDetails)];
        return originals.Count == 0 ? running : originals;
    }

    private static List<CopyQuality> Largest(List<CopyQuality> running)
    {
        long most = running.Max(copy => copy.Pixels);
        return [.. running.Where(copy => copy.Pixels == most)];
    }
}
