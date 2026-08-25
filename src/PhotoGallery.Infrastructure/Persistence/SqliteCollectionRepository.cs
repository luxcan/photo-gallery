using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="ICollectionRepository"/>
public sealed class SqliteCollectionRepository : ICollectionRepository
{
    private readonly GalleryDbContext _db;

    public SqliteCollectionRepository(GalleryDbContext db) => _db = db;

    public async Task<IReadOnlyList<DatedPhoto>> GetCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Everything the clusterer can place on a timeline, minus everything
        // somebody has already spoken for. A photograph in a collection the user
        // kept or made is theirs; the pass rebuilds only what it proposed.
        return await _db.Assets
            .AsNoTracking()
            .Where(asset => asset.Status == AssetStatus.Ready
                         && asset.QuarantinedUtc == null
                         && asset.ThumbnailName != null
                         && asset.TakenUtc != null
                         && !_db.CollectionMembers.Any(member =>
                                member.AssetId == asset.Id
                                && member.Collection!.Origin != CollectionOrigin.Proposed))
            .OrderBy(asset => asset.TakenUtc)
            .Select(asset => new DatedPhoto(
                asset.Id, asset.TakenUtc!.Value, asset.Latitude, asset.Longitude))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> GetRejectionsAsync(
        CancellationToken cancellationToken = default)
    {
        List<CollectionRejection> rejections = await _db.CollectionRejections
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rejections
            .GroupBy(rejection => rejection.ProposalKey, StringComparer.Ordinal)
            .ToDictionary(
                span => span.Key,
                span => (IReadOnlyList<int>)[.. span.Select(rejection => rejection.AssetId)],
                StringComparer.Ordinal);
    }

    public async Task<int> SaveProposalsAsync(
        IReadOnlyList<ProposedCollection> proposals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        List<Collection> existing = await _db.Collections
            .Include(collection => collection.Members)
            .Where(collection => collection.Origin == CollectionOrigin.Proposed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, Collection> byKey = existing
            .Where(collection => collection.ProposalKey is not null)
            .ToDictionary(collection => collection.ProposalKey!, StringComparer.Ordinal);

        var offered = new HashSet<string>(
            proposals.Select(proposal => proposal.ProposalKey), StringComparer.Ordinal);

        // A proposal nobody answered and the clusterer no longer makes is not a
        // question any more. One the user kept or made is not touched here at
        // all - it was never in this list.
        foreach (Collection stale in existing.Where(collection =>
            collection.ProposalKey is null || !offered.Contains(collection.ProposalKey)))
        {
            _db.Collections.Remove(stale);
        }

        DateTime now = DateTime.UtcNow;
        int written = 0;

        foreach (ProposedCollection proposal in proposals)
        {
            if (byKey.TryGetValue(proposal.ProposalKey, out Collection? row))
            {
                Rewrite(row, proposal, now);
            }
            else
            {
                _db.Collections.Add(New(proposal, now));
            }

            written++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return written;
    }

    public async Task<IReadOnlyList<CollectionSummary>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Collections
            .AsNoTracking()
            .OrderByDescending(collection => collection.StartUtc)
            .Select(collection => new CollectionSummary(
                collection.Id,
                collection.Name,
                collection.StartUtc,
                collection.EndUtc,
                collection.Kind,
                collection.Origin,
                collection.Members.Count,
                _db.Assets
                    .Where(asset => asset.Id == collection.CoverAssetId)
                    .Select(asset => asset.ThumbnailName)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> GetMembersAsync(
        int collectionId, CancellationToken cancellationToken = default)
    {
        return await _db.CollectionMembers
            .AsNoTracking()
            .Where(member => member.CollectionId == collectionId)
            .Join(
                _db.Assets.AsNoTracking(),
                member => member.AssetId,
                asset => asset.Id,
                (member, asset) => new { asset.Id, asset.TakenUtc, asset.ModifiedUtc })
            .OrderBy(row => row.TakenUtc ?? row.ModifiedUtc)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // No span and no key: it is not a proposal, and no rebuild will ever
        // match it, remove it or rename it.
        var collection = new Collection
        {
            Name = name.Trim(),
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow,
            Kind = CollectionKind.Period,
            Origin = CollectionOrigin.Made,
            ProposalKey = null,
            WasRenamed = true,
            BuiltUtc = DateTime.UtcNow,
        };

        _db.Collections.Add(collection);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return collection.Id;
    }

    public async Task AcceptAsync(int collectionId, CancellationToken cancellationToken = default)
    {
        Collection? collection = await _db.Collections
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null || collection.Origin != CollectionOrigin.Proposed)
        {
            return;
        }

        collection.Origin = CollectionOrigin.Accepted;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DismissAsync(int collectionId, CancellationToken cancellationToken = default)
    {
        Collection? collection = await _db.Collections
            .Include(row => row.Members)
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        // Dismissing is remembering, one row per photograph. The collection row
        // itself goes: a dismissed row would hold its photographs hostage
        // against the one-collection rule for ever, and two stores for one
        // decision is how they come to disagree.
        if (collection.ProposalKey is string span)
        {
            await RememberAsync(
                span,
                [.. collection.Members.Select(member => member.AssetId)],
                cancellationToken).ConfigureAwait(false);
        }

        _db.Collections.Remove(collection);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameAsync(
        int collectionId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Collection? collection = await _db.Collections
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        collection.Name = name.Trim();
        collection.WasRenamed = true;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int collectionId, CancellationToken cancellationToken = default)
    {
        Collection? collection = await _db.Collections
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        _db.Collections.Remove(collection);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionMoveResult> AddAsync(
        int collectionId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return CollectionMoveResult.Nothing;
        }

        // Where they are now, before anything moves, so the answer can say what
        // they came out of.
        var leaving = await _db.CollectionMembers
            .AsNoTracking()
            .Where(member => assetIds.Contains(member.AssetId)
                          && member.CollectionId != collectionId)
            .Join(
                _db.Collections.AsNoTracking(),
                member => member.CollectionId,
                collection => collection.Id,
                (member, collection) => new { member.AssetId, collection.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<CollectionMember> existing = await _db.CollectionMembers
            .Where(member => assetIds.Contains(member.AssetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Delete then insert, in that order and in one save: the key refuses a
        // second row for a photograph rather than overwriting the first.
        _db.CollectionMembers.RemoveRange(existing);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;
        _db.CollectionMembers.AddRange(assetIds.Distinct().Select(assetId => new CollectionMember
        {
            AssetId = assetId,
            CollectionId = collectionId,
            AddedUtc = now,
        }));

        await EnsureCoverAsync(collectionId, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CollectionMoveResult(
            assetIds.Distinct().Count(),
            leaving.Count,
            [.. leaving.Select(row => row.Name).Distinct(StringComparer.Ordinal).Order()]);
    }

    public async Task RemoveAsync(
        int collectionId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return;
        }

        Collection? collection = await _db.Collections
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        List<CollectionMember> members = await _db.CollectionMembers
            .Where(member => member.CollectionId == collectionId
                          && assetIds.Contains(member.AssetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _db.CollectionMembers.RemoveRange(members);

        // Taking a photograph out of something the app suggested is a rejection
        // and is remembered. Taking one out of a collection somebody made
        // themselves is not - they are rearranging their own shelf.
        if (collection.Origin != CollectionOrigin.Made && collection.ProposalKey is string span)
        {
            await RememberAsync(
                span,
                [.. members.Select(member => member.AssetId)],
                cancellationToken).ConfigureAwait(false);
        }

        await EnsureCoverAsync(collectionId, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records that these photographs do not belong in that run of days.</summary>
    private async Task RememberAsync(
        string span, IReadOnlyList<int> assetIds, CancellationToken cancellationToken)
    {
        if (assetIds.Count == 0)
        {
            return;
        }

        HashSet<int> already = [.. await _db.CollectionRejections
            .Where(rejection => rejection.ProposalKey == span
                             && assetIds.Contains(rejection.AssetId))
            .Select(rejection => rejection.AssetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)];

        DateTime now = DateTime.UtcNow;
        _db.CollectionRejections.AddRange(assetIds
            .Distinct()
            .Where(assetId => !already.Contains(assetId))
            .Select(assetId => new CollectionRejection
            {
                AssetId = assetId,
                ProposalKey = span,
                RejectedUtc = now,
            }));
    }

    /// <summary>
    /// Gives a collection a cover if it has none, or has lost the one it had.
    /// </summary>
    /// <remarks>
    /// The middle photograph of the span, which is a better sample of an
    /// occasion than either end: the first is arriving and the last is leaving.
    /// </remarks>
    private async Task EnsureCoverAsync(int collectionId, CancellationToken cancellationToken)
    {
        Collection? collection = await _db.Collections
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        List<int> members = await _db.CollectionMembers
            .Where(member => member.CollectionId == collectionId)
            .Join(
                _db.Assets,
                member => member.AssetId,
                asset => asset.Id,
                (member, asset) => new { asset.Id, asset.TakenUtc })
            .OrderBy(row => row.TakenUtc)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        collection.CoverAssetId = members.Count == 0 ? 0 : members[members.Count / 2];
    }

    private static Collection New(ProposedCollection proposal, DateTime now)
    {
        var collection = new Collection
        {
            Name = proposal.Name,
            StartUtc = proposal.StartUtc,
            EndUtc = proposal.EndUtc,
            Kind = proposal.Kind,
            Origin = CollectionOrigin.Proposed,
            PlaceId = proposal.PlaceId,
            CoverAssetId = proposal.CoverAssetId,
            ProposalKey = proposal.ProposalKey,
            BuiltUtc = now,
        };

        foreach (int assetId in proposal.AssetIds)
        {
            collection.Members.Add(new CollectionMember { AssetId = assetId, AddedUtc = now });
        }

        return collection;
    }

    /// <summary>
    /// Brings an existing proposal up to date without disturbing what the user
    /// has said about it.
    /// </summary>
    /// <remarks>
    /// A name the user typed is never written over, which is the difference
    /// between a suggestion and an imposition.
    /// </remarks>
    private void Rewrite(Collection row, ProposedCollection proposal, DateTime now)
    {
        if (!row.WasRenamed)
        {
            row.Name = proposal.Name;
        }

        row.StartUtc = proposal.StartUtc;
        row.EndUtc = proposal.EndUtc;
        row.Kind = proposal.Kind;
        row.PlaceId = proposal.PlaceId;
        row.CoverAssetId = proposal.CoverAssetId;
        row.BuiltUtc = now;

        var wanted = new HashSet<int>(proposal.AssetIds);

        foreach (CollectionMember gone in row.Members.Where(m => !wanted.Contains(m.AssetId)))
        {
            _db.CollectionMembers.Remove(gone);
        }

        var held = new HashSet<int>(row.Members.Select(member => member.AssetId));
        foreach (int assetId in proposal.AssetIds.Where(assetId => !held.Contains(assetId)))
        {
            row.Members.Add(new CollectionMember { AssetId = assetId, AddedUtc = now });
        }
    }
}
