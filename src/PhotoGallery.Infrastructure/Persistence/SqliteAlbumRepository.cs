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

        // A clean slate before anything is read, and this is not tidiness.
        //
        // One scope serves a whole scan, so this context has been used by every
        // phase before this one, and some of them leave what they loaded
        // tracked. Others delete rows with ExecuteDelete, which is a statement
        // the change tracker is never told about - and deleting an asset takes
        // its album memberships with it through the database's own cascade.
        //
        // Put those two together and the Include below hands back a membership
        // that no longer exists: a query does not refresh an entity the context
        // is already tracking, it returns the instance it has. Rewrite then asks
        // for that row to be deleted, the delete matches nothing, and the whole
        // pass ends in "expected to affect 1 row(s), but actually affected 0".
        // That is exactly how a six-minute scan was lost.
        //
        // Nothing is discarded by this: every write in this layer saves before
        // it returns, so what is tracked here is what was already written.
        _db.ChangeTracker.Clear();

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

        // One pass, two rules. A proposal that carries a shelf was answered by
        // whoever put it there; a proposal nobody answered and the clusterer no
        // longer makes is not a question any more. An album the user kept or
        // made is not touched here at all - it was never in this list.
        foreach (Album album in existing)
        {
            // Older libraries shelved a suggestion without keeping it, and no
            // migration can tell such a row from an unanswered one. Left as a
            // proposal it is counted by the collection and left off the wall,
            // which shows only albums the user owns, and the removal below is a
            // delete rather than a tombstone. Waiting for its key to go stale
            // would never heal it: shelving an album changes no photograph, so
            // the clusterer offers that same key again on every later scan.
            if (album.CollectionId is not null)
            {
                album.Origin = AlbumOrigin.Accepted;
                continue;
            }

            if (album.ProposalKey is null || !offered.Contains(album.ProposalKey))
            {
                _db.Albums.Remove(album);
            }
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

        // And released again, so the phases after this one start as clean as
        // this one insisted on starting.
        _db.ChangeTracker.Clear();

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
                    .FirstOrDefault(),
                album.CollectionId))
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

                    // Which picture the album shows, read the same way the wall
                    // reads it. This was null, because the viewer only needed
                    // the album's name - and then the viewer learned to offer
                    // to change the cover, which it cannot do without knowing
                    // whether the open photograph is already the one. It looked
                    // exactly like a button that did nothing: the choice was
                    // written and the screen went on offering to make it.
                    _db.Assets
                        .Where(asset => asset.Id == album.CoverAssetId)
                        .Select(asset => asset.ThumbnailName)
                        .FirstOrDefault()))
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
                (member, asset) => asset)
            // The date the rest of the app files the picture under. This was a
            // spelling of that rule with the creation date left out, which put
            // a photograph in one order here and another in the grid above it.
            .OrderBy(AssetDates.Taken)
            .ThenBy(asset => asset.Id)
            .Select(asset => asset.Id)
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

                         // One album each, but only a person's claim counts.
                         // A photograph the clusterer swept into a suggestion
                         // is not spoken for - a suggestion is a question
                         // nobody has answered yet - and letting it hold that
                         // photograph back would put the app's own guess above
                         // an album somebody made themselves. Half of this
                         // library sits in suggestions, so the other reading
                         // leaves a rule that can almost never find anything.
                         //
                         // Keeping one is what moves it: the key on
                         // AlbumMembers allows a photograph one row, so
                         // AddAsync takes it out of the suggestion on the way
                         // in. This is the same test the clusterer's own feed
                         // applies in GetCandidatesAsync, and for the same
                         // reason.
                         //
                         // The first half of the condition is what stops an
                         // album offering back what it already holds. That
                         // matters when the album doing the asking is itself a
                         // suggestion, which the second half would wave
                         // through.
                         && !_db.AlbumMembers.Any(member =>
                                member.AssetId == asset.Id
                                && (member.AlbumId == albumId
                                    || member.Album!.Origin != AlbumOrigin.Proposed))

                         // And what was refused for this album is not
                         // offered for it a second time.
                         && !_db.AlbumRejections.Any(rejection =>
                                rejection.AssetId == asset.Id && rejection.ProposalKey == span));

        if (rule.From is not null || rule.To is not null)
        {
            // The same date the grid files the picture under, fallback and all,
            // rather than the capture date alone. Requiring a capture date read
            // as the careful choice and was the opposite: not one video in the
            // library carries one, so a day rule could not match a video while
            // every screen showed those videos sitting on that day. Where the
            // rule lives, and what it costs, is written out in
            // AssetDates.TakenBetween.
            fitting = fitting.Where(AssetDates.TakenBetween(rule.From, rule.To));
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
            // Newest first, by the same date the filter just used. This was a
            // third spelling of the rule that left out the creation date, so a
            // photograph could be offered in one order and filed in another.
            .OrderByDescending(AssetDates.Taken)
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
                (member, album) => new
                {
                    member.AssetId,
                    AlbumId = album.Id,
                    album.Name,
                    album.Origin,
                })
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

        // Saved before any cover is chosen, because choosing one reads the
        // memberships back out of the database and a pending insert is not
        // there to be read. An album whose first photographs all arrived in one
        // press would otherwise be left with no cover at all - which is what
        // used to happen, and was hidden by the way a refusal was written: it
        // added the photographs and took them straight out again, and the
        // taking out chose a cover on its way past.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await EnsureCoverAsync(albumId, cancellationToken).ConfigureAwait(false);

        await SettleWhatTheyLeftAsync(
            [.. leaving.Select(row => (row.AlbumId, row.Origin)).Distinct()],
            cancellationToken).ConfigureAwait(false);

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

        // Written before a cover is chosen, for the reason AddAsync saves before
        // choosing one: a cover is picked by reading the memberships back out of
        // the database, and a pending delete is still there to be read. The
        // photograph being taken out could therefore be chosen as the cover of
        // the album it was just taken out of - and a chosen cover looked as
        // though it were still in the album when it was not.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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

    public async Task RefuseAsync(
        int albumId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return;
        }

        bool exists = await _db.Albums
            .AnyAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return;
        }

        await RememberAsync(
            await SpanKeyAsync(albumId, cancellationToken).ConfigureAwait(false),
            assetIds,
            cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetCoverAsync(
        int albumId, int assetId, CancellationToken cancellationToken = default)
    {
        Album? album = await _db.Albums
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return false;
        }

        // An album cannot show a photograph it does not hold. The screen only
        // offers this for a picture that is already in one, so this is the guard
        // for every other way in rather than a case anybody will meet.
        bool holdsIt = await _db.AlbumMembers
            .AnyAsync(
                member => member.AlbumId == albumId && member.AssetId == assetId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!holdsIt)
        {
            return false;
        }

        album.CoverAssetId = assetId;
        album.CoverChosenUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return true;
    }

    /// <summary>
    /// Puts right the albums that photographs have just been taken out of.
    /// </summary>
    /// <remarks>
    /// Two things, because one move causes both. A cover may have been among
    /// the photographs that left, and an album showing a photograph it no
    /// longer holds is the kind of wrongness a person sees on the wall before
    /// they see anything else.
    ///
    /// <para>And a suggestion the move emptied is not a question any more, so
    /// its row goes. Nothing would ever arrive to fill it again: the
    /// clusterer's feed skips photographs an album somebody made has spoken
    /// for, so the days this one was built from are not offered a second time.
    /// No refusal is written for the photographs that left, because they went
    /// somewhere better and that is the opposite of being refused. An album the
    /// user made or kept is never removed here however empty the move leaves
    /// it - that row is theirs, and only they may throw it away.</para>
    /// </remarks>
    private async Task SettleWhatTheyLeftAsync(
        IReadOnlyList<(int AlbumId, AlbumOrigin Origin)> sources,
        CancellationToken cancellationToken)
    {
        foreach ((int albumId, AlbumOrigin origin) in sources)
        {
            bool anyLeft = await _db.AlbumMembers
                .AnyAsync(member => member.AlbumId == albumId, cancellationToken)
                .ConfigureAwait(false);

            if (anyLeft || origin != AlbumOrigin.Proposed)
            {
                await EnsureCoverAsync(albumId, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Album? emptied = await _db.Albums
                .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
                .ConfigureAwait(false);

            if (emptied is not null)
            {
                _db.Albums.Remove(emptied);
            }
        }
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
                (member, asset) => asset)
            // Not the capture date on its own: SQLite sorts a null first, so
            // every video and every undated photograph bunched at the front
            // and the middle of this list stopped being the middle of the
            // album's span. Videos only started arriving in albums in numbers
            // when a day rule learned to match them.
            .OrderBy(AssetDates.Taken)
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (members.Count == 0)
        {
            album.CoverAssetId = 0;

            // Nothing is left to have chosen, so the next photograph to arrive
            // gets the rule rather than a decision about an empty album.
            album.CoverChosenUtc = null;
            return;
        }

        // A cover somebody chose is left exactly where it is, which is the whole
        // point of recording that they chose it: this method runs on every add
        // and every remove, so without this the choice would survive until the
        // next photograph joined the album and would then be replaced in
        // silence.
        if (album.CoverChosenUtc is not null && members.Contains(album.CoverAssetId))
        {
            return;
        }

        // The choice was about a photograph this album no longer holds - taken
        // out, or set aside as a duplicate. The rule is a better answer than a
        // picture that is not in here any more, and forgetting the choice is
        // what lets it be one again later.
        album.CoverChosenUtc = null;

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
