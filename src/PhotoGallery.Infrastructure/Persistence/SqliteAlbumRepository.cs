using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IAlbumRepository"/>
public sealed class SqliteAlbumRepository : IAlbumRepository
{
    private readonly GalleryDbContext _db;

    public SqliteAlbumRepository(GalleryDbContext db) => _db = db;

    public async Task<IReadOnlyList<DatedPhoto>> GetCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Everything the clusterer can place on a timeline, minus everything
        // somebody has already spoken for. A photograph in an album the user
        // kept or made is theirs; the pass rebuilds only what it proposed.
        return await _db.Assets
            .AsNoTracking()
            .Where(asset => asset.Status == AssetStatus.Ready
                         && asset.QuarantinedUtc == null
                         && asset.ThumbnailName != null
                         && asset.TakenUtc != null
                         && !_db.AlbumMembers.Any(member =>
                                member.AssetId == asset.Id
                                && member.Album!.Origin != AlbumOrigin.Proposed))
            .OrderBy(asset => asset.TakenUtc)
            .Select(asset => new DatedPhoto(
                asset.Id, asset.TakenUtc!.Value, asset.Latitude, asset.Longitude))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> GetRejectionsAsync(
        CancellationToken cancellationToken = default)
    {
        List<AlbumRejection> rejections = await _db.AlbumRejections
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
        IReadOnlyList<ProposedAlbum> proposals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        List<Album> existing = await _db.Albums
            .Include(album => album.Members)
            .Where(album => album.Origin == AlbumOrigin.Proposed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, Album> byKey = existing
            .Where(album => album.ProposalKey is not null)
            .ToDictionary(album => album.ProposalKey!, StringComparer.Ordinal);

        var offered = new HashSet<string>(
            proposals.Select(proposal => proposal.ProposalKey), StringComparer.Ordinal);

        // A proposal nobody answered and the clusterer no longer makes is not a
        // question any more. One the user kept or made is not touched here at
        // all - it was never in this list.
        foreach (Album stale in existing.Where(album =>
            album.ProposalKey is null || !offered.Contains(album.ProposalKey)))
        {
            _db.Albums.Remove(stale);
        }

        DateTime now = DateTime.UtcNow;
        int written = 0;

        foreach (ProposedAlbum proposal in proposals)
        {
            if (byKey.TryGetValue(proposal.ProposalKey, out Album? row))
            {
                Rewrite(row, proposal, now);
            }
            else
            {
                _db.Albums.Add(New(proposal, now));
            }

            written++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return written;
    }

    public async Task<IReadOnlyList<AlbumSummary>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Albums
            .AsNoTracking()
            .OrderByDescending(album => album.StartUtc)
            .Select(album => new AlbumSummary(
                album.Id,
                album.Name,
                album.StartUtc,
                album.EndUtc,
                album.Kind,
                album.Origin,
                album.Members.Count,
                _db.Assets
                    .Where(asset => asset.Id == album.CoverAssetId)
                    .Select(asset => asset.ThumbnailName)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AlbumSummary?> FindForAssetAsync(
        int assetId, CancellationToken cancellationToken = default)
    {
        return await _db.AlbumMembers
            .AsNoTracking()
            .Where(member => member.AssetId == assetId)
            .Join(
                _db.Albums.AsNoTracking(),
                member => member.AlbumId,
                album => album.Id,
                (member, album) => new AlbumSummary(
                    album.Id,
                    album.Name,
                    album.StartUtc,
                    album.EndUtc,
                    album.Kind,
                    album.Origin,
                    album.Members.Count,
                    null))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> GetMembersAsync(
        int albumId, CancellationToken cancellationToken = default)
    {
        return await _db.AlbumMembers
            .AsNoTracking()
            .Where(member => member.AlbumId == albumId)
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
        var album = new Album
        {
            Name = name.Trim(),
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow,
            Kind = AlbumKind.Period,
            Origin = AlbumOrigin.Made,
            ProposalKey = null,
            NamedUtc = DateTime.UtcNow,
            BuiltUtc = DateTime.UtcNow,
        };

        _db.Albums.Add(album);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return album.Id;
    }

    public async Task<AlbumRule> GetRuleAsync(
        int albumId, CancellationToken cancellationToken = default)
    {
        Album? album = await _db.Albums
            .AsNoTracking()
            .Include(row => row.RulePeople)
            .Include(row => row.RulePlaces)
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return AlbumRule.None;
        }

        return new AlbumRule(
            album.RuleFromUtc is DateTime from ? DateOnly.FromDateTime(from) : null,
            album.RuleToUtc is DateTime to ? DateOnly.FromDateTime(to) : null,
            [.. album.RulePeople.Select(rule => rule.PersonId).Order()],
            [.. album.RulePlaces.Select(rule => rule.PlaceId).Order()]);
    }

    public async Task SetRuleAsync(
        int albumId, AlbumRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Album? album = await _db.Albums
            .Include(row => row.RulePeople)
            .Include(row => row.RulePlaces)
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return;
        }

        album.RuleFromUtc = rule.From?.ToDateTime(TimeOnly.MinValue);
        album.RuleToUtc = rule.To?.ToDateTime(TimeOnly.MinValue);

        // Replaced rather than merged: the rule the user is looking at is the
        // whole rule, so a person they took out has to go.
        _db.AlbumRulePeople.RemoveRange(album.RulePeople);
        _db.AlbumRulePlaces.RemoveRange(album.RulePlaces);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _db.AlbumRulePeople.AddRange(rule.PersonIds.Distinct().Select(personId =>
            new AlbumRulePerson { AlbumId = albumId, PersonId = personId }));
        _db.AlbumRulePlaces.AddRange(rule.PlaceIds.Distinct().Select(placeId =>
            new AlbumRulePlace { AlbumId = albumId, PlaceId = placeId }));

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> SuggestAsync(
        int albumId, CancellationToken cancellationToken = default)
    {
        AlbumRule rule = await GetRuleAsync(albumId, cancellationToken)
            .ConfigureAwait(false);

        if (!rule.IsSomething)
        {
            // No rule, nothing to look for. Answering with the whole library
            // would be the opposite of a suggestion.
            return [];
        }

        string span = await SpanKeyAsync(albumId, cancellationToken).ConfigureAwait(false);

        IQueryable<Asset> fitting = _db.Assets
            .AsNoTracking()
            .Where(asset => asset.Status == AssetStatus.Ready
                         && asset.QuarantinedUtc == null
                         && asset.ThumbnailName != null

                         // One album each: what is spoken for stays where
                         // it is rather than being offered away from it.
                         && !_db.AlbumMembers.Any(member => member.AssetId == asset.Id)

                         // And what was refused for this album is not
                         // offered for it a second time.
                         && !_db.AlbumRejections.Any(rejection =>
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
    /// The key a refusal is remembered under for this album.
    /// </summary>
    /// <remarks>
    /// A proposal is remembered by its run of days, because the row itself is
    /// rebuilt. An album somebody made is permanent, so its own id is a
    /// stable name - and the two are kept in one table because they answer the
    /// same question: never offer this photograph here again.
    /// </remarks>
    private async Task<string> SpanKeyAsync(int albumId, CancellationToken cancellationToken)
    {
        string? key = await _db.Albums
            .AsNoTracking()
            .Where(row => row.Id == albumId)
            .Select(row => row.ProposalKey)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return key ?? $"made:{albumId}";
    }

    public async Task AcceptAsync(int albumId, CancellationToken cancellationToken = default)
    {
        Album? album = await _db.Albums
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null || album.Origin != AlbumOrigin.Proposed)
        {
            return;
        }

        album.Origin = AlbumOrigin.Accepted;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DismissAsync(int albumId, CancellationToken cancellationToken = default)
    {
        Album? album = await _db.Albums
            .Include(row => row.Members)
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return;
        }

        // Dismissing is remembering, one row per photograph. The album row
        // itself goes: a dismissed row would hold its photographs hostage
        // against the one-album rule for ever, and two stores for one
        // decision is how they come to disagree.
        await RememberAsync(
            await SpanKeyAsync(albumId, cancellationToken).ConfigureAwait(false),
            [.. album.Members.Select(member => member.AssetId)],
            cancellationToken).ConfigureAwait(false);

        _db.Albums.Remove(album);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameAsync(
        int albumId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Album? album = await _db.Albums
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return;
        }

        album.Name = name.Trim();
        album.NamedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int albumId, CancellationToken cancellationToken = default)
    {
        Album? album = await _db.Albums
            .Include(row => row.Members)
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return;
        }

        // The photographs come out and the row stays as a tombstone, for the
        // reason a deleted person leaves one: without it the next merge from a
        // machine that still holds the album puts it back. Its members go with
        // it, so they are free to join another - a tombstone holding photographs
        // against the one-album rule would be the hostage the dismissal path
        // already refuses to take.
        album.Members.Clear();
        album.DeletedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AlbumAddResult> AddAsync(
        int albumId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return AlbumAddResult.Nothing;
        }

        // Where they are now, before anything moves, so the answer can say what
        // they came out of.
        var leaving = await _db.AlbumMembers
            .AsNoTracking()
            .Where(member => assetIds.Contains(member.AssetId)
                          && member.AlbumId != albumId)
            .Join(
                _db.Albums.AsNoTracking(),
                member => member.AlbumId,
                album => album.Id,
                (member, album) => new { member.AssetId, album.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AlbumMember> existing = await _db.AlbumMembers
            .Where(member => assetIds.Contains(member.AssetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Delete then insert, in that order and in one save: the key refuses a
        // second row for a photograph rather than overwriting the first.
        _db.AlbumMembers.RemoveRange(existing);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;
        _db.AlbumMembers.AddRange(assetIds.Distinct().Select(assetId => new AlbumMember
        {
            AssetId = assetId,
            AlbumId = albumId,
            AddedUtc = now,
        }));

        await EnsureCoverAsync(albumId, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AlbumAddResult(
            assetIds.Distinct().Count(),
            leaving.Count,
            [.. leaving.Select(row => row.Name).Distinct(StringComparer.Ordinal).Order()]);
    }

    public async Task RemoveAsync(
        int albumId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return;
        }

        Album? album = await _db.Albums
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return;
        }

        List<AlbumMember> members = await _db.AlbumMembers
            .Where(member => member.AlbumId == albumId
                          && assetIds.Contains(member.AssetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _db.AlbumMembers.RemoveRange(members);

        // Taking a photograph out of something the app suggested is a rejection
        // and is remembered. Taking one out of an album somebody made
        // themselves is not - they are rearranging their own shelf.
        // Taking a photograph out is a refusal wherever it happens: out of a
        // suggestion it means "not this occasion", and out of an album with
        // a rule it means "not this one, whatever the rule says" - otherwise the
        // next press of Find photos that fit would offer it straight back.
        await RememberAsync(
            await SpanKeyAsync(albumId, cancellationToken).ConfigureAwait(false),
            [.. members.Select(member => member.AssetId)],
            cancellationToken).ConfigureAwait(false);

        await EnsureCoverAsync(albumId, cancellationToken).ConfigureAwait(false);
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

        HashSet<int> already = [.. await _db.AlbumRejections
            .Where(rejection => rejection.ProposalKey == span
                             && assetIds.Contains(rejection.AssetId))
            .Select(rejection => rejection.AssetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)];

        DateTime now = DateTime.UtcNow;
        _db.AlbumRejections.AddRange(assetIds
            .Distinct()
            .Where(assetId => !already.Contains(assetId))
            .Select(assetId => new AlbumRejection
            {
                AssetId = assetId,
                ProposalKey = span,
                RejectedUtc = now,
            }));
    }

    /// <summary>
    /// Gives an album a cover if it has none, or has lost the one it had.
    /// </summary>
    /// <remarks>
    /// One with people in it, and the middle of the span only when there are
    /// none - the same rule the pass uses, because an album whose cover
    /// changed depending on which code path last touched it would be worse than
    /// either rule on its own.
    /// </remarks>
    private async Task EnsureCoverAsync(int albumId, CancellationToken cancellationToken)
    {
        Album? album = await _db.Albums
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return;
        }

        List<int> members = await _db.AlbumMembers
            .Where(member => member.AlbumId == albumId)
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
            album.CoverAssetId = 0;
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

        album.CoverAssetId = withFaces?.AssetId ?? members[members.Count / 2];
    }

    private static Album New(ProposedAlbum proposal, DateTime now)
    {
        var album = new Album
        {
            Name = proposal.Name,
            StartUtc = proposal.StartUtc,
            EndUtc = proposal.EndUtc,
            Kind = proposal.Kind,
            Origin = AlbumOrigin.Proposed,
            PlaceId = proposal.PlaceId,
            CoverAssetId = proposal.CoverAssetId,
            ProposalKey = proposal.ProposalKey,
            BuiltUtc = now,
        };

        foreach (int assetId in proposal.AssetIds)
        {
            album.Members.Add(new AlbumMember { AssetId = assetId, AddedUtc = now });
        }

        return album;
    }

    /// <summary>
    /// Brings an existing proposal up to date without disturbing what the user
    /// has said about it.
    /// </summary>
    /// <remarks>
    /// A name the user typed is never written over, which is the difference
    /// between a suggestion and an imposition.
    /// </remarks>
    private void Rewrite(Album row, ProposedAlbum proposal, DateTime now)
    {
        if (row.NamedUtc is null)
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

        foreach (AlbumMember gone in row.Members.Where(m => !wanted.Contains(m.AssetId)))
        {
            _db.AlbumMembers.Remove(gone);
        }

        var held = new HashSet<int>(row.Members.Select(member => member.AssetId));
        foreach (int assetId in proposal.AssetIds.Where(assetId => !held.Contains(assetId)))
        {
            row.Members.Add(new AlbumMember { AssetId = assetId, AddedUtc = now });
        }
    }
}
