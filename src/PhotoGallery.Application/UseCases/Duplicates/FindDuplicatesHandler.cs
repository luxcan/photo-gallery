using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>
/// Looks through the library for the same photograph stored more than once.
/// </summary>
/// <remarks>
/// Both kinds come out of facts already on the row, so this pass opens no files
/// at all. The content hash is taken while the bytes are in memory during the
/// prepare pass - the one moment they are there - and the perceptual hash comes
/// off the same decode. Twelve years of copying between phones, cards and
/// folders is answered in under a second.
///
/// <para>The two kinds are kept apart to the end. Byte-identical is a proof and
/// can be approved in bulk; visually alike is a question, because a perceptual
/// hash cannot tell a re-saved copy from the next frame of a burst.</para>
/// </remarks>
public sealed class FindDuplicatesHandler
{
    private readonly IDuplicateRepository _duplicates;

    public FindDuplicatesHandler(IDuplicateRepository duplicates) => _duplicates = duplicates;

    public async Task<DuplicateScan> HandleAsync(
        int nearThreshold = NearDuplicates.DefaultThreshold,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Asset> candidates =
            await _duplicates.GetCandidatesAsync(cancellationToken).ConfigureAwait(false);

        List<DuplicateSet> exact = [.. Exact(candidates).Select(group => SetOf(DuplicateKind.Exact, group))];

        cancellationToken.ThrowIfCancellationRequested();

        // Anything proved identical is already answered, so the perceptual pass
        // does not offer it a second time in a weaker form.
        HashSet<int> settled =
        [
            .. exact.SelectMany(set => set.Members).Select(member => member.AssetId),
        ];

        List<DuplicateSet> near =
        [
            .. NearDuplicates
                .Group(candidates.Where(asset => !settled.Contains(asset.Id)), nearThreshold)
                .Select(group => SetOf(DuplicateKind.Near, group)),
        ];

        cancellationToken.ThrowIfCancellationRequested();

        int exactSets = await _duplicates
            .ReplaceAsync(DuplicateKind.Exact, exact, cancellationToken).ConfigureAwait(false);
        int nearSets = await _duplicates
            .ReplaceAsync(DuplicateKind.Near, near, cancellationToken).ConfigureAwait(false);

        return new DuplicateScan(
            candidates.Count,
            exactSets,
            Redundant(exact),
            Reclaimable(exact),
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

    private static DuplicateSet SetOf(DuplicateKind kind, IReadOnlyList<Asset> group)
    {
        var set = new DuplicateSet { Kind = kind, DetectedUtc = DateTime.UtcNow };

        foreach (Asset asset in group)
        {
            set.Members.Add(new DuplicateMember { AssetId = asset.Id, Asset = asset });
        }

        set.AssignRoles();

        // Measured from the keeper rather than from the group's leader: the
        // number the review screen shows is "how far is this from the one I am
        // keeping?", which is the only comparison the user can act on.
        Asset keeper = set.Members
            .First(member => member.Role == DuplicateRole.Keeper)
            .Asset!;

        foreach (DuplicateMember member in set.Members)
        {
            member.Distance = kind == DuplicateKind.Near
                ? NearDuplicates.DistanceFrom(keeper, member.Asset!)
                : 0;
        }

        return set;
    }

    private static int Redundant(IEnumerable<DuplicateSet> sets) =>
        sets.Sum(set => set.Members.Count(member => member.Role == DuplicateRole.Redundant));

    private static long Reclaimable(IEnumerable<DuplicateSet> sets) =>
        sets.Sum(set => set.RedundantBytes);
}
