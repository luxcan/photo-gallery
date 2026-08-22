using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IDuplicateRepository"/>
public sealed class SqliteDuplicateRepository : IDuplicateRepository
{
    private readonly GalleryDbContext _db;

    public SqliteDuplicateRepository(GalleryDbContext db) => _db = db;

    public async Task<IReadOnlyList<Asset>> GetCandidatesAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Assets
            .AsNoTracking()
            .Where(asset => asset.Kind == AssetKind.Photo
                         && asset.QuarantinedUtc == null
                         && asset.ContentHash != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<int> ReplaceAsync(
        DuplicateKind kind,
        IReadOnlyList<DuplicateSet> sets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sets);

        // Only the outstanding questions. A resolved set records a decision the
        // user made, and a pass has no business revising that.
        await _db.Set<DuplicateSet>()
            .Where(set => set.Kind == kind && !set.IsResolved)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Sets whose members are all already accounted for by a decision are not
        // asked about again - otherwise every pass would re-offer the copies the
        // user has already dealt with.
        HashSet<int> settled = [.. await SettledAssetIdsAsync(cancellationToken).ConfigureAwait(false)];

        var fresh = new List<DuplicateSet>();
        foreach (DuplicateSet set in sets)
        {
            if (set.Members.Any(member => settled.Contains(member.AssetId)))
            {
                continue;
            }

            var copy = new DuplicateSet
            {
                Kind = set.Kind,
                DetectedUtc = set.DetectedUtc,
                IsResolved = false,
            };

            foreach (DuplicateMember member in set.Members)
            {
                copy.Members.Add(new DuplicateMember
                {
                    AssetId = member.AssetId,
                    Role = member.Role,
                    Distance = member.Distance,
                });
            }

            fresh.Add(copy);
        }

        _db.Set<DuplicateSet>().AddRange(fresh);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return fresh.Count;
    }

    public async Task<IReadOnlyList<DuplicateSetView>> GetAsync(
        DuplicateKind kind, CancellationToken cancellationToken = default)
    {
        List<DuplicateSet> sets = await _db.Set<DuplicateSet>()
            .AsNoTracking()
            .Include(set => set.Members)
            .Where(set => set.Kind == kind && !set.IsResolved)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ViewsOfAsync(sets, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DuplicateSetView?> FindAsync(
        int setId, CancellationToken cancellationToken = default)
    {
        DuplicateSet? set = await _db.Set<DuplicateSet>()
            .AsNoTracking()
            .Include(candidate => candidate.Members)
            .FirstOrDefaultAsync(candidate => candidate.Id == setId, cancellationToken)
            .ConfigureAwait(false);

        return set is null
            ? null
            : (await ViewsOfAsync([set], cancellationToken).ConfigureAwait(false))
                .FirstOrDefault();
    }

    public async Task MarkResolvedAsync(
        int setId, bool resolved, CancellationToken cancellationToken = default) =>
        await _db.Set<DuplicateSet>()
            .Where(set => set.Id == setId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(set => set.IsResolved, resolved),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task SetKeeperAsync(
        int setId, int assetId, CancellationToken cancellationToken = default)
    {
        List<DuplicateMember> members = await _db.Set<DuplicateMember>()
            .Where(member => member.DuplicateSetId == setId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Silently doing nothing would leave the screen showing a keeper the
        // index disagrees with, which is worse than the request failing.
        if (!members.Any(member => member.AssetId == assetId))
        {
            return;
        }

        foreach (DuplicateMember member in members)
        {
            member.Role = member.AssetId == assetId
                ? DuplicateRole.Keeper
                : DuplicateRole.Redundant;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    public async Task SetQuarantinedAsync(
        IReadOnlyList<int> assetIds,
        DateTime? quarantinedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        // Chunked: SQLite caps parameters per statement, and setting aside a
        // whole pass's worth of copies is exactly when that limit is met.
        foreach (int[] chunk in assetIds.Distinct().Chunk(400))
        {
            await _db.Assets
                .Where(asset => chunk.Contains(asset.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(asset => asset.QuarantinedUtc, quarantinedUtc),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<QuarantinedCopy>> GetQuarantinedAsync(
        CancellationToken cancellationToken = default)
    {
        Dictionary<int, string> roots = await _db.Set<PhotoSource>()
            .AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Path, cancellationToken)
            .ConfigureAwait(false);

        var rows = await _db.Assets
            .AsNoTracking()
            .Where(asset => asset.QuarantinedUtc != null)
            .OrderByDescending(asset => asset.QuarantinedUtc)
            .Select(asset => new
            {
                asset.Id, asset.PhotoSourceId, asset.RelativePath,
                asset.Length, asset.QuarantinedUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new QuarantinedCopy(
                row.Id,
                row.PhotoSourceId,
                row.RelativePath,
                roots.TryGetValue(row.PhotoSourceId, out string? root)
                    ? Path.Combine(root, row.RelativePath)
                    : string.Empty,
                row.Length,
                row.QuarantinedUtc!.Value)),
        ];
    }

    /// <summary>
    /// Assets already set aside, or already kept as part of a resolved set.
    /// </summary>
    private async Task<List<int>> SettledAssetIdsAsync(CancellationToken cancellationToken)
    {
        List<int> quarantined = await _db.Assets
            .AsNoTracking()
            .Where(asset => asset.QuarantinedUtc != null)
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<int> resolved = await _db.Set<DuplicateMember>()
            .AsNoTracking()
            .Where(member => member.DuplicateSet!.IsResolved)
            .Select(member => member.AssetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. quarantined, .. resolved];
    }

    /// <summary>
    /// Joins the sets to the assets they name, in one query rather than per set.
    /// </summary>
    private async Task<IReadOnlyList<DuplicateSetView>> ViewsOfAsync(
        IReadOnlyList<DuplicateSet> sets, CancellationToken cancellationToken)
    {
        if (sets.Count == 0)
        {
            return [];
        }

        int[] assetIds = [.. sets.SelectMany(set => set.Members).Select(member => member.AssetId)];

        Dictionary<int, string> roots = await _db.Set<PhotoSource>()
            .AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Path, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, Asset> assets = await _db.Assets
            .AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id))
            .ToDictionaryAsync(asset => asset.Id, cancellationToken)
            .ConfigureAwait(false);

        var views = new List<DuplicateSetView>(sets.Count);
        foreach (DuplicateSet set in sets)
        {
            var copies = new List<DuplicateCopy>(set.Members.Count);
            foreach (DuplicateMember member in set.Members)
            {
                if (!assets.TryGetValue(member.AssetId, out Asset? asset))
                {
                    continue;
                }

                copies.Add(new DuplicateCopy(
                    asset.Id,
                    asset.PhotoSourceId,
                    asset.RelativePath,
                    roots.TryGetValue(asset.PhotoSourceId, out string? root)
                        ? Path.Combine(root, asset.RelativePath)
                        : string.Empty,
                    asset.ThumbnailName,
                    asset.Length,
                    asset.Width,
                    asset.Height,
                    member.Role,
                    member.Distance,
                    asset.ContentHash,
                    asset.TakenUtc,
                    asset.ModifiedUtc));
            }

            // A set whose keeper has gone is not a question any more.
            if (copies.Count > 1 && copies.Any(copy => copy.Role == DuplicateRole.Keeper))
            {
                views.Add(new DuplicateSetView(
                    set.Id,
                    set.Kind,
                    [.. copies.OrderBy(copy => copy.Role).ThenBy(copy => copy.Distance)]));
            }
        }

        // Biggest saving first: it is the order in which the work is worth doing.
        return [.. views.OrderByDescending(view => view.RedundantBytes)];
    }
}
