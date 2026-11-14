using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>
/// Looks through the library for the same photograph stored more than once.
/// </summary>
/// <remarks>
/// Both kinds come out of facts already on the row, so finding them opens no
/// files at all. The content hash is taken while the bytes are in memory during
/// the prepare pass - the one moment they are there - and the perceptual hash
/// comes off the same decode.
///
/// <para>The two kinds are kept apart to the end. Byte-identical is a proof and
/// can be approved in bulk; visually alike is a question, because a perceptual
/// hash cannot tell a re-saved copy from the next frame of a burst.</para>
///
/// <para>Then, for the visually alike only, the copies are measured from their
/// cached previews and the keeper ladder suggests one, or none (PRP 14). An
/// identical set is never measured and never gets a suggestion: the same bytes
/// are the same photograph, and either copy is the one to keep. Stopping during
/// the measuring leaves the sets as they were and keeps what was measured, so
/// the next run carries on.</para>
/// </remarks>
public sealed class FindDuplicatesHandler
{
    private readonly IDuplicateRepository _duplicates;
    private readonly IPhotoQualityRepository _quality;
    private readonly CopyMeasurer _measurer;
    private readonly IModelStore _models;

    public FindDuplicatesHandler(
        IDuplicateRepository duplicates,
        IPhotoQualityRepository quality,
        CopyMeasurer measurer,
        IModelStore models)
    {
        _duplicates = duplicates;
        _quality = quality;
        _measurer = measurer;
        _models = models;
    }

    public async Task<DuplicateScan> HandleAsync(
        int nearThreshold = NearDuplicates.DefaultThreshold,
        IProgress<DuplicateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new DuplicateProgress(DuplicateProgress.Finding, 0, 0));

        IReadOnlyList<Asset> candidates =
            await _duplicates.GetCandidatesAsync(cancellationToken).ConfigureAwait(false);

        List<IReadOnlyList<Asset>> exactGroups = [.. Exact(candidates)];

        cancellationToken.ThrowIfCancellationRequested();

        // Anything proved identical is already answered, so the perceptual pass
        // does not offer it a second time in a weaker form.
        HashSet<int> settled = [.. exactGroups.SelectMany(group => group).Select(asset => asset.Id)];

        IReadOnlyList<IReadOnlyList<Asset>> nearGroups = NearDuplicates
            .Group(candidates.Where(asset => !settled.Contains(asset.Id)), nearThreshold);

        int[] nearCopies = [.. nearGroups.SelectMany(group => group).Select(asset => asset.Id)];
        bool readEyes = EyeReading.IsAvailable(_models);

        await _measurer
            .MeasureAsync(nearCopies, readEyes, progress, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<int, CopyQuality> qualities =
            await _quality.GetQualitiesAsync(nearCopies, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        List<DuplicateSet> exact = [.. exactGroups.Select(group => SetOf(DuplicateKind.Exact, group, null))];
        List<DuplicateSet> near =
        [
            .. nearGroups.Select(group => SetOf(
                DuplicateKind.Near,
                group,
                KeeperPolicy.Choose([.. group.Select(asset => qualities[asset.Id])], readEyes).KeeperId)),
        ];

        int exactSets = await _duplicates
            .ReplaceAsync(DuplicateKind.Exact, exact, cancellationToken).ConfigureAwait(false);
        int nearSets = await _duplicates
            .ReplaceAsync(DuplicateKind.Near, near, cancellationToken).ConfigureAwait(false);

        return new DuplicateScan(
            candidates.Count,
            exactSets,
            Copies(exact),
            Bytes(exact),
            nearSets,
            Redundant(near),
            Reclaimable(near));
    }

    /// <summary>
    /// Files with identical bytes, grouped by the hash already on the row.
    /// </summary>
    /// <remarks>
    /// No size pre-filter and no reading. The hash is a whole-file SHA-256 taken
    /// during preparation, so this is a grouping rather than a comparison - and
    /// two files sharing one cannot differ in length either.
    /// </remarks>
    private static IEnumerable<IReadOnlyList<Asset>> Exact(IReadOnlyList<Asset> candidates) =>
        candidates
            .Where(asset => !string.IsNullOrEmpty(asset.ContentHash))
            .GroupBy(asset => asset.ContentHash!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => (IReadOnlyList<Asset>)[.. group.OrderBy(asset => asset.Id)]);

    private static DuplicateSet SetOf(DuplicateKind kind, IReadOnlyList<Asset> group, int? keeperId)
    {
        var set = new DuplicateSet { Kind = kind, DetectedUtc = DateTime.UtcNow };

        foreach (Asset asset in group)
        {
            set.Members.Add(new DuplicateMember { AssetId = asset.Id, Asset = asset });
        }

        set.Suggest(keeperId);

        // Measured from the suggested copy rather than from the group's leader:
        // the number the screen shows is "how far is this from the one the app
        // would keep?". With nothing suggested, from the leader, which is the
        // copy the group was gathered around.
        Asset reference = keeperId is int id
            ? group.First(asset => asset.Id == id)
            : group[0];

        foreach (DuplicateMember member in set.Members)
        {
            member.Distance = kind == DuplicateKind.Near
                ? NearDuplicates.DistanceFrom(reference, member.Asset!)
                : 0;
        }

        return set;
    }

    private static int Redundant(IEnumerable<DuplicateSet> sets) =>
        sets.Sum(set => set.Members.Count(member => member.Role == DuplicateRole.Redundant));

    private static long Reclaimable(IEnumerable<DuplicateSet> sets) =>
        sets.Sum(set => set.RedundantBytes);

    /// <summary>
    /// The copies an identical set could give up, all but one each. Counted
    /// rather than read off roles, because no copy of an identical set is
    /// suggested - whichever one is kept, the rest are spare.
    /// </summary>
    private static int Copies(IEnumerable<DuplicateSet> sets) =>
        sets.Sum(set => set.Members.Count - 1);

    private static long Bytes(IEnumerable<DuplicateSet> sets) =>
        sets.Sum(set => set.Members.Skip(1).Sum(member => member.Asset?.Length ?? 0L));
}
