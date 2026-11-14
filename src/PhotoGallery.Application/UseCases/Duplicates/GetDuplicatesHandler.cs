using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>Everything the duplicates screen shows.</summary>
/// <remarks>
/// The suggestion on a "Looks the same" set is worked out here from the
/// measurements as they stand, not read back from the pass that found the set.
/// What the card says - which rung decided, whose eyes are closed - is only
/// true of the measurements it came from, and those move on their own: the face
/// pass replaces faces, a name is given, the eye model is installed or removed.
/// One rule asked at the moment of showing cannot disagree with its own
/// captions.
/// </remarks>
public sealed class GetDuplicatesHandler
{
    private readonly IDuplicateRepository _duplicates;
    private readonly IPhotoQualityRepository _quality;
    private readonly IModelStore _models;

    public GetDuplicatesHandler(
        IDuplicateRepository duplicates, IPhotoQualityRepository quality, IModelStore models)
    {
        _duplicates = duplicates;
        _quality = quality;
        _models = models;
    }

    public async Task<DuplicateBoard> HandleAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DuplicateSetView> exact =
            await _duplicates.GetAsync(DuplicateKind.Exact, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<DuplicateSetView> near =
            await _duplicates.GetAsync(DuplicateKind.Near, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<QuarantinedCopy> setAside =
            await _duplicates.GetQuarantinedAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<int, CopyQuality> qualities = await _quality
            .GetQualitiesAsync([.. near.SelectMany(set => set.Copies).Select(copy => copy.AssetId)], cancellationToken)
            .ConfigureAwait(false);

        bool readEyes = near.Count > 0 && EyeReading.IsAvailable(_models);

        // Sorted again once judged, biggest saving first, because what deciding
        // a set would reclaim depends on the copy suggested now.
        return new DuplicateBoard(
            [.. exact.Select(Undecided)],
            [
                .. near
                    .Select(set => Judged(set, qualities, readEyes))
                    .OrderByDescending(set => set.RedundantBytes),
            ],
            setAside);
    }

    /// <summary>
    /// An identical set with no copy suggested, whatever an older pass wrote.
    /// </summary>
    /// <remarks>
    /// Sets found before PRP 14 carry a keeper chosen by the folder rule, which
    /// is gone. Until the next look for duplicates rewrites them, they are shown
    /// as what they are - the same file, twice.
    /// </remarks>
    private static DuplicateSetView Undecided(DuplicateSetView set) =>
        set with { Copies = [.. set.Copies.Select(copy => copy with { Role = DuplicateRole.Undecided })] };

    /// <summary>One "Looks the same" set with the ladder's answer on every copy.</summary>
    private static DuplicateSetView Judged(
        DuplicateSetView set, IReadOnlyDictionary<int, CopyQuality> qualities, bool readEyes)
    {
        List<DuplicateCopy> measurable = [.. set.Copies.Where(copy => qualities.ContainsKey(copy.AssetId))];
        if (measurable.Count == 0)
        {
            return Undecided(set);
        }

        KeeperChoice choice = KeeperPolicy.Choose(
            [.. measurable.Select(copy => qualities[copy.AssetId])], readEyes);

        // Distances from the copy suggested now, or from the copy the set was
        // gathered around - its lowest id - when none is.
        DuplicateCopy reference = set.Copies.FirstOrDefault(copy => copy.AssetId == choice.KeeperId)
                                  ?? set.Copies.MinBy(copy => copy.AssetId)!;

        IEnumerable<DuplicateCopy> judged = set.Copies.Select(copy =>
        {
            int distance = DistanceBetween(reference, copy);
            if (!qualities.TryGetValue(copy.AssetId, out CopyQuality? quality))
            {
                return copy with { Role = DuplicateRole.Undecided, Distance = distance };
            }

            CopyVerdict verdict = choice.For(copy.AssetId);
            bool kept = choice.KeeperId == copy.AssetId;

            return copy with
            {
                Distance = distance,
                Role = choice.KeeperId is null
                    ? DuplicateRole.Undecided
                    : kept ? DuplicateRole.Keeper : DuplicateRole.Redundant,
                DecidedBy = kept ? choice.DecidedBy : verdict.LostOn,
                Exposure = verdict.Exposure,
                ClosedFaces = verdict.ClosedFaces,
                PreviewWidth = quality.PreviewWidth,
                PreviewHeight = quality.PreviewHeight,
            };
        });

        return set with { Copies = [.. judged.OrderBy(copy => copy.Role).ThenBy(copy => copy.Distance)] };
    }

    /// <summary>
    /// How many bits of the two fingerprints differ, or what the pass stored
    /// when either copy has none to compare.
    /// </summary>
    private static int DistanceBetween(DuplicateCopy reference, DuplicateCopy copy) =>
        reference.Fingerprint is PerceptualHash from && copy.Fingerprint is PerceptualHash to
            ? from.DistanceTo(to)
            : copy.Distance;
}

/// <summary>
/// The two kinds, kept apart, and what has already been set aside.
/// </summary>
/// <remarks>
/// Never one merged list. Byte-identical is a proof and can be approved in bulk;
/// visually alike is a question, because a perceptual hash cannot tell a
/// re-saved copy from the next frame of a burst - and those bursts are often
/// photographs worth keeping.
/// </remarks>
public sealed record DuplicateBoard(
    IReadOnlyList<DuplicateSetView> Exact,
    IReadOnlyList<DuplicateSetView> Near,
    IReadOnlyList<QuarantinedCopy> SetAside)
{
    public long ExactBytes => Exact.Sum(set => set.RedundantBytes);

    public long NearBytes => Near.Sum(set => set.RedundantBytes);

    public long SetAsideBytes => SetAside.Sum(copy => copy.Length);

    public bool HasAnything => Exact.Count + Near.Count > 0;
}
