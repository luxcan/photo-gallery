using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.People;

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

    public async Task<CollectionSummary?> FindForAssetAsync(
        int assetId, CancellationToken cancellationToken = default)
    {
        return await _db.CollectionMembers
            .AsNoTracking()
            .Where(member => member.AssetId == assetId)
            .Join(
                _db.Collections.AsNoTracking(),
                member => member.CollectionId,
                collection => collection.Id,
                (member, collection) => new CollectionSummary(
                    collection.Id,
                    collection.Name,
                    collection.StartUtc,
                    collection.EndUtc,
                    collection.Kind,
                    collection.Origin,
                    collection.Members.Count,
                    null))
            .FirstOrDefaultAsync(cancellationToken)
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

    public async Task<CollectionRule> GetRuleAsync(
        int collectionId, CancellationToken cancellationToken = default)
    {
        Collection? collection = await _db.Collections
            .AsNoTracking()
            .Include(row => row.RulePeople)
            .Include(row => row.RulePlaces)
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return CollectionRule.None;
        }

        return new CollectionRule(
            collection.RuleFromUtc is DateTime from ? DateOnly.FromDateTime(from) : null,
            collection.RuleToUtc is DateTime to ? DateOnly.FromDateTime(to) : null,
            [.. collection.RulePeople.Select(rule => rule.PersonId).Order()],
            [.. collection.RulePlaces.Select(rule => rule.PlaceId).Order()]);
    }

    public async Task SetRuleAsync(
        int collectionId, CollectionRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Collection? collection = await _db.Collections
            .Include(row => row.RulePeople)
            .Include(row => row.RulePlaces)
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        collection.RuleFromUtc = rule.From?.ToDateTime(TimeOnly.MinValue);
        collection.RuleToUtc = rule.To?.ToDateTime(TimeOnly.MinValue);

        // Replaced rather than merged: the rule the user is looking at is the
        // whole rule, so a person they took out has to go.
        _db.CollectionRulePeople.RemoveRange(collection.RulePeople);
        _db.CollectionRulePlaces.RemoveRange(collection.RulePlaces);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _db.CollectionRulePeople.AddRange(rule.PersonIds.Distinct().Select(personId =>
            new CollectionRulePerson { CollectionId = collectionId, PersonId = personId }));
        _db.CollectionRulePlaces.AddRange(rule.PlaceIds.Distinct().Select(placeId =>
            new CollectionRulePlace { CollectionId = collectionId, PlaceId = placeId }));

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> SuggestAsync(
        int collectionId, CancellationToken cancellationToken = default)
    {
        CollectionRule rule = await GetRuleAsync(collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (!rule.IsSomething)
        {
            // No rule, nothing to look for. Answering with the whole library
            // would be the opposite of a suggestion.
            return [];
        }

        string span = await SpanKeyAsync(collectionId, cancellationToken).ConfigureAwait(false);

        IQueryable<Asset> fitting = _db.Assets
            .AsNoTracking()
            .Where(asset => asset.Status == AssetStatus.Ready
                         && asset.QuarantinedUtc == null
                         && asset.ThumbnailName != null

                         // One collection each: what is spoken for stays where
                         // it is rather than being offered away from it.
                         && !_db.CollectionMembers.Any(member => member.AssetId == asset.Id)

                         // And what was refused for this collection is not
                         // offered for it a second time.
                         && !_db.CollectionRejections.Any(rejection =>
                                rejection.AssetId == asset.Id && rejection.ProposalKey == span));

        if (rule.From is DateOnly from)
        {
            DateTime start = from.ToDateTime(TimeOnly.MinValue);
            fitting = fitting.Where(asset => asset.TakenUtc != null && asset.TakenUtc >= start);
        }

        if (rule.To is DateOnly to)
        {
            // The last day is included whole - somebody who types one date means
            // that day, not the instant it begins.
            DateTime end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            fitting = fitting.Where(asset => asset.TakenUtc != null && asset.TakenUtc < end);
        }

        if (rule.PlaceIds.Count > 0)
        {
            // Any of them: a photograph was taken in one place.
            fitting = fitting.Where(asset =>
                asset.PlaceId != null && rule.PlaceIds.Contains(asset.PlaceId.Value));
        }

        if (rule.PersonIds.Count > 0)
        {
            // Any of them, as with the places above. Asking for all of them at
            // once reads well and finds almost nothing: three names wants the
            // photographs where all three happen to stand together, which in a
            // family library is a handful out of thousands. An album naming
            // three people is about those people, not about the occasions they
            // were photographed as a set.
            //
            // Only confirmed faces count - a proposal is a question the user has
            // not answered, and answering it by quietly using it would make the
            // question pointless.
            int[] wanted = [.. rule.PersonIds];

            fitting = fitting.Where(asset => _db.Faces.Any(face =>
                face.AssetId == asset.Id
                && _db.FaceAssignments.Any(assignment =>
                    assignment.FaceId == face.Id
                    && wanted.Contains(assignment.PersonId)
                    && assignment.Source == AssignmentSource.Confirmed)));
        }

        return await fitting
            .OrderByDescending(asset => asset.TakenUtc ?? asset.ModifiedUtc)
            .ThenByDescending(asset => asset.Id)
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The key a refusal is remembered under for this collection.
    /// </summary>
    /// <remarks>
    /// A proposal is remembered by its run of days, because the row itself is
    /// rebuilt. A collection somebody made is permanent, so its own id is a
    /// stable name - and the two are kept in one table because they answer the
    /// same question: never offer this photograph here again.
    /// </remarks>
    private async Task<string> SpanKeyAsync(int collectionId, CancellationToken cancellationToken)
    {
        string? key = await _db.Collections
            .AsNoTracking()
            .Where(row => row.Id == collectionId)
            .Select(row => row.ProposalKey)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return key ?? $"made:{collectionId}";
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
        await RememberAsync(
            await SpanKeyAsync(collectionId, cancellationToken).ConfigureAwait(false),
            [.. collection.Members.Select(member => member.AssetId)],
            cancellationToken).ConfigureAwait(false);

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
        // Taking a photograph out is a refusal wherever it happens: out of a
        // suggestion it means "not this occasion", and out of a collection with
        // a rule it means "not this one, whatever the rule says" - otherwise the
        // next press of Find photos that fit would offer it straight back.
        await RememberAsync(
            await SpanKeyAsync(collectionId, cancellationToken).ConfigureAwait(false),
            [.. members.Select(member => member.AssetId)],
            cancellationToken).ConfigureAwait(false);

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
    /// One with people in it, and the middle of the span only when there are
    /// none - the same rule the pass uses, because a collection whose cover
    /// changed depending on which code path last touched it would be worse than
    /// either rule on its own.
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

        if (members.Count == 0)
        {
            collection.CoverAssetId = 0;
            return;
        }

        var withFaces = await _db.Faces
            .AsNoTracking()
            .Where(face => members.Contains(face.AssetId))
            .GroupBy(face => face.AssetId)
            .Select(photo => new { AssetId = photo.Key, Faces = photo.Count() })
            .OrderByDescending(photo => photo.Faces)
            .ThenBy(photo => photo.AssetId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        collection.CoverAssetId = withFaces?.AssetId ?? members[members.Count / 2];
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
