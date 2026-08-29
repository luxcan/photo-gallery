using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IDecisionRepository"/>
/// <remarks>
/// Carries out a plan the merge has already settled. Nothing here decides
/// anything: every contest was resolved by a pure function against two decision
/// sets, and what arrives is a list of differences.
///
/// <para>Saved in stages rather than in one transaction, and that is deliberate.
/// A merge is stoppable like every other pass in this app, and the whole state
/// is read again on the next run - so what has been applied is applied, what has
/// not is picked up, and a stop costs nothing but the rest of this run.</para>
/// </remarks>
public sealed class SqliteDecisionRepository : IDecisionRepository
{
    private readonly GalleryDbContext _db;

    public SqliteDecisionRepository(GalleryDbContext db) => _db = db;

    public async Task<MergeOutcome> ApplyAsync(
        MergePlan plan,
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // First, and before anything is keyed on a source. A rename changes what
        // every key in this library means, so applying one answer under the old
        // identity and the next under the new one would file the same photograph
        // in two places.
        await ApplyLinksAsync(plan, cancellationToken).ConfigureAwait(false);

        progress?.Report(new MergeProgress("People", 0, plan.People.Count));

        (int gained, int renamed, int deleted) =
            await ApplyPeopleAsync(plan, cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return MergeOutcome.Nothing with { WasCancelled = true };
        }

        progress?.Report(new MergeProgress("Names", 0, plan.Answers.Count));

        (int namesGained, int namesReplaced) =
            await ApplyAnswersAsync(plan, cancellationToken).ConfigureAwait(false);

        int setAside = await ApplyStrangersAsync(plan, cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return new MergeOutcome(
                gained, renamed, deleted, namesGained, namesReplaced, setAside,
                0, 0, 0, 0, [], plan.Joins, plan.Refused, WasCancelled: true);
        }

        progress?.Report(new MergeProgress("Albums", 0, plan.Albums.Count + plan.Moves.Count));

        int albums = await ApplyAlbumsAsync(plan, cancellationToken).ConfigureAwait(false);
        int moved = await ApplyMovesAsync(plan, cancellationToken).ConfigureAwait(false);
        await ApplyRejectionsAsync(plan, cancellationToken).ConfigureAwait(false);
        await ApplyErasAsync(plan, cancellationToken).ConfigureAwait(false);

        progress?.Report(new MergeProgress("Waiting answers", 0, plan.Held.Count));

        int held = await HoldAsync(plan.Held, cancellationToken).ConfigureAwait(false);

        return new MergeOutcome(
            gained, renamed, deleted, namesGained, namesReplaced, setAside,
            plan.Turns.Count, albums, moved, held,
            plan.Moves, plan.Joins, plan.Refused,
            cancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// Takes the pairings other machines have confirmed, and adopts the identity
    /// they settle on.
    /// </summary>
    /// <remarks>
    /// The rename reaches everything keyed on a source, which is the photo
    /// source itself and the answers still waiting for their photographs. It
    /// reaches nothing else: every other key in this library is a row id, and
    /// rows do not move.
    /// </remarks>
    private async Task ApplyLinksAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Links.Count > 0)
        {
            HashSet<(Guid, Guid)> here =
            [
                .. await _db.PairedSources
                    .Select(pair => ValueTuple.Create(pair.Left, pair.Right))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false),
            ];

            foreach (SourceLink link in plan.Links)
            {
                SourceLink ordered = link.Ordered();

                if (here.Add((ordered.Left, ordered.Right)))
                {
                    _db.PairedSources.Add(new PairedSource
                    {
                        Left = ordered.Left,
                        Right = ordered.Right,
                        PairedUtc = ordered.PairedUtc,
                        DecidedBy = ordered.DecidedBy,
                    });
                }
            }
        }

        foreach ((Guid from, Guid to) in plan.Renames)
        {
            await _db.PhotoSources
                .Where(source => source.SharedId == from)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(source => source.SharedId, to),
                    cancellationToken)
                .ConfigureAwait(false);

            // Answers parked under the old identity are about the same
            // photographs; left behind, they would wait for a folder that no
            // longer exists under that name.
            await _db.HeldDecisions
                .Where(held => held.SharedSourceId == from)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(held => held.SharedSourceId, to),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    public async Task RecordTurnsAsync(
        IReadOnlyList<PhotoTurn> turns,
        IReadOnlyDictionary<AssetKey, int> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(rows);

        foreach (PhotoTurn turn in turns)
        {
            if (!rows.TryGetValue(turn.Photo, out int assetId))
            {
                continue;
            }

            await _db.Assets
                .Where(asset => asset.Id == assetId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(asset => asset.Rotation, turn.Rotation)
                        .SetProperty(asset => asset.RotatedUtc, turn.DecidedUtc)
                        .SetProperty(asset => asset.RotatedBy, turn.DecidedBy),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        _db.ChangeTracker.Clear();
    }

    public async Task RememberAsync(
        MachineIdentity machine,
        DateTime mergedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Peer? peer = await _db.Peers
            .FirstOrDefaultAsync(row => row.MachineId == machine.Id, cancellationToken)
            .ConfigureAwait(false);

        if (peer is null)
        {
            _db.Peers.Add(new Peer
            {
                MachineId = machine.Id,
                Name = machine.Name,
                LastMergedUtc = mergedUtc,
            });
        }
        else
        {
            // The name is theirs to change, so it is taken every time rather
            // than only when the row is new.
            peer.Name = machine.Name;
            peer.LastMergedUtc = mergedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    // ---------------------------------------------------------------- people

    private async Task<(int Gained, int Renamed, int Deleted)> ApplyPeopleAsync(
        MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.People.Count == 0)
        {
            return (0, 0, 0);
        }

        Dictionary<Guid, Person> here = await _db.People
            .IgnoreQueryFilters()
            .ToDictionaryAsync(person => person.PublicId, cancellationToken)
            .ConfigureAwait(false);

        int gained = 0;
        int renamed = 0;
        int deleted = 0;

        foreach (SharedPerson settled in plan.People)
        {
            if (!here.TryGetValue(settled.PublicId, out Person? person))
            {
                _db.People.Add(new Person
                {
                    PublicId = settled.PublicId,
                    DisplayName = Free(settled.DisplayName, here.Values),
                    BirthYear = settled.BirthYear,
                    UpdatedUtc = settled.UpdatedUtc,
                    DeletedUtc = settled.DeletedUtc,
                });

                gained++;
                continue;
            }

            if (settled.DeletedUtc is not null && person.DeletedUtc is null)
            {
                await ForgetAsync(person.Id, cancellationToken).ConfigureAwait(false);
                person.DeletedUtc = settled.DeletedUtc;
                deleted++;
            }

            if (!string.Equals(person.DisplayName, settled.DisplayName, StringComparison.Ordinal))
            {
                person.DisplayName = Free(settled.DisplayName, here.Values);
                renamed++;
            }

            person.BirthYear = settled.BirthYear ?? person.BirthYear;
            person.UpdatedUtc = settled.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return (gained, renamed, deleted);
    }

    /// <summary>
    /// A name nobody living here is already using.
    /// </summary>
    /// <remarks>
    /// Two people with one name is a real thing in a family and the merge
    /// deliberately keeps them apart, offering a join rather than performing one -
    /// but the index holds names unique among the living, so the second Ana to
    /// arrive has to be distinguishable or the whole merge fails on her. She is
    /// numbered rather than refused, and the join offer is what settles it
    /// properly.
    /// </remarks>
    private static string Free(string wanted, IEnumerable<Person> here)
    {
        var taken = new HashSet<string>(
            here.Where(person => person.DeletedUtc is null).Select(person => person.DisplayName),
            StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(wanted))
        {
            return wanted;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{wanted} ({suffix})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Takes everything a person was, leaving the row as a tombstone.</summary>
    private async Task ForgetAsync(int personId, CancellationToken cancellationToken)
    {
        await _db.FaceAssignments
            .Where(assignment => assignment.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await _db.Set<PersonEra>()
            .Where(era => era.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // ----------------------------------------------------------------- faces

    private async Task<(int Gained, int Replaced)> ApplyAnswersAsync(
        MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Answers.Count == 0 && plan.Withdrawn.Count == 0)
        {
            return (0, 0);
        }

        Dictionary<FaceKey, int> faces =
            await FaceRowsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, int> people = await _db.People
            .IgnoreQueryFilters()
            .ToDictionaryAsync(person => person.PublicId, person => person.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (FaceAnswer gone in plan.Withdrawn)
        {
            if (faces.TryGetValue(gone.Face, out int faceId)
                && people.TryGetValue(gone.Person, out int personId))
            {
                await _db.FaceAssignments
                    .Where(a => a.FaceId == faceId && a.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        Dictionary<(int Face, int Person), FaceAssignment> existing = await _db.FaceAssignments
            .ToDictionaryAsync(a => (a.FaceId, a.PersonId), cancellationToken)
            .ConfigureAwait(false);

        int gained = 0;
        int replaced = 0;

        foreach (FaceAnswer answer in plan.Answers)
        {
            if (!faces.TryGetValue(answer.Face, out int faceId)
                || !people.TryGetValue(answer.Person, out int personId))
            {
                continue;
            }

            if (existing.TryGetValue((faceId, personId), out FaceAssignment? was))
            {
                was.Source = answer.Source;
                was.DecidedUtc = answer.DecidedUtc;
                was.DecidedBy = answer.DecidedBy;
                was.Score = null;
                replaced++;
                continue;
            }

            _db.FaceAssignments.Add(new FaceAssignment
            {
                FaceId = faceId,
                PersonId = personId,
                Source = answer.Source,
                DecidedUtc = answer.DecidedUtc,
                DecidedBy = answer.DecidedBy,
            });

            gained++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return (gained, replaced);
    }

    private async Task<int> ApplyStrangersAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Strangers.Count == 0 && plan.Recognised.Count == 0)
        {
            return 0;
        }

        Dictionary<FaceKey, int> faces =
            await FaceRowsAsync(cancellationToken).ConfigureAwait(false);

        int setAside = 0;

        foreach (StrangerFace stranger in plan.Strangers)
        {
            if (!faces.TryGetValue(stranger.Face, out int faceId))
            {
                continue;
            }

            // Anything said about a face that is nobody was said about the wrong
            // thing, so it goes with it - the same rule as setting one aside by
            // hand, and the reason the plan does not have to list every name it
            // is quietly taking.
            await _db.FaceAssignments
                .Where(assignment => assignment.FaceId == faceId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await _db.Faces
                .Where(face => face.Id == faceId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(face => face.IgnoredUtc, stranger.DecidedUtc)
                        .SetProperty(face => face.IgnoredBy, stranger.DecidedBy),
                    cancellationToken)
                .ConfigureAwait(false);

            setAside++;
        }

        foreach (FaceKey recognised in plan.Recognised)
        {
            if (faces.TryGetValue(recognised, out int faceId))
            {
                await _db.Faces
                    .Where(face => face.Id == faceId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(face => face.IgnoredUtc, (DateTime?)null)
                            .SetProperty(face => face.IgnoredBy, Guid.Empty),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        _db.ChangeTracker.Clear();
        return setAside;
    }

    // ---------------------------------------------------------------- albums

    private async Task<int> ApplyAlbumsAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Albums.Count == 0)
        {
            return 0;
        }

        List<Collection> here = await _db.Collections
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int changed = 0;

        foreach (SharedAlbum settled in plan.Albums)
        {
            Collection? album = Match(here, settled);

            if (album is null)
            {
                // Only an album somebody made is created from a merge. A proposal
                // is derived, so its row is this machine's own to build - what
                // travels about one is a name, and a name with nothing to sit on
                // waits for the rebuild that makes it.
                if (settled.Origin == CollectionOrigin.Proposed || settled.DeletedUtc is not null)
                {
                    continue;
                }

                _db.Collections.Add(new Collection
                {
                    PublicId = settled.PublicId,
                    Name = settled.Name,
                    StartUtc = DateTime.UtcNow,
                    EndUtc = DateTime.UtcNow,
                    Kind = CollectionKind.Period,
                    Origin = settled.Origin,
                    ProposalKey = settled.ProposalKey,
                    NamedUtc = settled.NamedUtc,
                    BuiltUtc = DateTime.UtcNow,
                });

                changed++;
                continue;
            }

            if (settled.DeletedUtc is not null && album.DeletedUtc is null)
            {
                await _db.CollectionMembers
                    .Where(member => member.CollectionId == album.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                album.DeletedUtc = settled.DeletedUtc;
                changed++;
            }

            if (!string.Equals(album.Name, settled.Name, StringComparison.Ordinal))
            {
                album.Name = settled.Name;
                album.NamedUtc = settled.NamedUtc;
                changed++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return changed;
    }

    /// <summary>
    /// The row an album names: by its run of days where it has one, and by its
    /// identity otherwise.
    /// </summary>
    private static Collection? Match(List<Collection> here, SharedAlbum album) =>
        album.ProposalKey is null
            ? here.FirstOrDefault(row => row.PublicId == album.PublicId)
            : here.FirstOrDefault(row => row.ProposalKey == album.ProposalKey)
              ?? here.FirstOrDefault(row => row.PublicId == album.PublicId);

    private async Task<int> ApplyMovesAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Moves.Count == 0)
        {
            return 0;
        }

        Dictionary<AssetKey, int> assets =
            await AssetRowsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, int> albums = await _db.Collections
            .IgnoreQueryFilters()
            .Where(album => album.DeletedUtc == null)
            .ToDictionaryAsync(album => album.PublicId, album => album.Id, cancellationToken)
            .ConfigureAwait(false);

        int moved = 0;

        foreach (AlbumMove move in plan.Moves)
        {
            if (!assets.TryGetValue(move.Photo, out int assetId)
                || !albums.TryGetValue(move.To, out int albumId))
            {
                continue;
            }

            // One delete and one insert, because a photograph's row is its whole
            // primary key in that table - the schema refuses a second rather
            // than overwriting it.
            await _db.CollectionMembers
                .Where(member => member.AssetId == assetId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            _db.CollectionMembers.Add(new CollectionMember
            {
                AssetId = assetId,
                CollectionId = albumId,
                AddedUtc = move.AddedUtc,
                AddedBy = move.DecidedBy,
            });

            moved++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return moved;
    }

    private async Task ApplyRejectionsAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Rejections.Count == 0)
        {
            return;
        }

        Dictionary<AssetKey, int> assets =
            await AssetRowsAsync(cancellationToken).ConfigureAwait(false);

        HashSet<(int, string)> here =
        [
            .. await _db.CollectionRejections
                .Select(r => ValueTuple.Create(r.AssetId, r.ProposalKey))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
        ];

        foreach (AlbumRejection rejection in plan.Rejections)
        {
            if (assets.TryGetValue(rejection.Photo, out int assetId)
                && here.Add((assetId, rejection.ProposalKey)))
            {
                _db.CollectionRejections.Add(new CollectionRejection
                {
                    AssetId = assetId,
                    ProposalKey = rejection.ProposalKey,
                    RejectedUtc = rejection.RejectedUtc,
                    RejectedBy = rejection.DecidedBy,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Plants the centroids this library cannot build for itself.
    /// </summary>
    /// <remarks>
    /// A seed, not a fact. The next rebuild replaces it from the first local
    /// confirmation in that stretch, which is exactly what should happen: this
    /// machine's own confirmed faces are worth more than an average of somebody
    /// else's.
    /// </remarks>
    private async Task ApplyErasAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Eras.Count == 0)
        {
            return;
        }

        Dictionary<Guid, int> people = await _db.People
            .ToDictionaryAsync(person => person.PublicId, person => person.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (SharedEra era in plan.Eras)
        {
            if (people.TryGetValue(era.Person, out int personId))
            {
                _db.PersonEras.Add(new PersonEra
                {
                    PersonId = personId,
                    FromUtc = era.FromUtc,
                    ToUtc = era.ToUtc,
                    Centroid = era.Centroid,
                    SampleCount = era.SampleCount,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    // ------------------------------------------------------------ still waiting

    /// <summary>
    /// Parks answers about photographs this library has not indexed, one row per
    /// answer.
    /// </summary>
    /// <remarks>
    /// One row per answer is what makes merging twice change nothing: without the
    /// key the table would grow with the number of times somebody pressed the
    /// button rather than with what anybody decided.
    /// </remarks>
    private async Task<int> HoldAsync(HeldAnswers held, CancellationToken cancellationToken)
    {
        if (held.Count == 0)
        {
            return 0;
        }

        Dictionary<(Guid, string, HeldDecisionKind, string), HeldDecision> waiting =
            await _db.HeldDecisions
                .ToDictionaryAsync(
                    row => (row.SharedSourceId, row.RelativePath, row.Kind, row.Part),
                    cancellationToken)
                .ConfigureAwait(false);

        foreach (Waiting answer in Flatten(held))
        {
            var key = (answer.Photo.SharedSourceId, answer.Photo.RelativePath, answer.Kind, answer.Part);

            if (waiting.TryGetValue(key, out HeldDecision? already))
            {
                // The later answer, exactly as it would be settled if the
                // photograph were here. An answer that waits is still an answer.
                if (already.DecidedUtc <= answer.DecidedUtc)
                {
                    already.Payload = answer.Payload;
                    already.FromMachine = answer.FromMachine;
                    already.DecidedUtc = answer.DecidedUtc;
                }

                continue;
            }

            HeldDecision row = new()
            {
                SharedSourceId = answer.Photo.SharedSourceId,
                RelativePath = answer.Photo.RelativePath,
                Kind = answer.Kind,
                Part = answer.Part,
                Payload = answer.Payload,
                FromMachine = answer.FromMachine,
                DecidedUtc = answer.DecidedUtc,
            };

            _db.HeldDecisions.Add(row);
            waiting[key] = row;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return held.Count;
    }

    /// <inheritdoc/>
    public async Task ReleaseAsync(
        HeldAnswers landed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(landed);

        if (landed.Count == 0)
        {
            return;
        }

        // Matched on the same key the rows were written under, so an answer that
        // landed is forgotten and one that merely looks like it - the same face
        // in the same photograph, a different person - is not.
        HashSet<(Guid, string, HeldDecisionKind, string)> done =
            [.. Flatten(landed).Select(a => (a.Photo.SharedSourceId, a.Photo.RelativePath, a.Kind, a.Part))];

        List<HeldDecision> rows = await _db.HeldDecisions
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (HeldDecision row in rows)
        {
            if (done.Contains((row.SharedSourceId, row.RelativePath, row.Kind, row.Part)))
            {
                _db.HeldDecisions.Remove(row);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    private static IEnumerable<Waiting> Flatten(HeldAnswers held)
    {
        // Keyed by the box and the person, not the box alone. One face carries
        // one name but several answers - refused as the elder child, confirmed
        // as the younger - and a key without the person would keep the last of
        // them and lose the rest while they waited.
        foreach (FaceAnswer answer in held.Answers)
        {
            yield return new Waiting(
                answer.Face.Photo,
                HeldDecisionKind.FaceAnswer,
                $"{answer.Face.Part}|{answer.Person:D}",
                Written(answer),
                answer.DecidedBy,
                answer.DecidedUtc);
        }

        // Its own kind rather than a nameless face answer, so that reading the
        // row back does not mean guessing from the shape of the JSON. A mark and
        // a name about one face are settled against each other when they land,
        // which is where that belongs - not here, where they are both only
        // waiting.
        foreach (StrangerFace stranger in held.Strangers)
        {
            yield return new Waiting(
                stranger.Face.Photo,
                HeldDecisionKind.Stranger,
                stranger.Face.Part,
                Written(stranger),
                stranger.DecidedBy,
                stranger.DecidedUtc);
        }

        foreach (PhotoTurn turn in held.Turns)
        {
            yield return new Waiting(
                turn.Photo,
                HeldDecisionKind.Turn,
                string.Empty,
                Written(turn),
                turn.DecidedBy,
                turn.DecidedUtc);
        }

        foreach (AlbumMembership membership in held.Memberships)
        {
            yield return new Waiting(
                membership.Photo,
                HeldDecisionKind.AlbumMembership,
                membership.Album.ToString("D"),
                Written(membership),
                membership.DecidedBy,
                membership.AddedUtc);
        }

        foreach (AlbumRejection rejection in held.Rejections)
        {
            yield return new Waiting(
                rejection.Photo,
                HeldDecisionKind.AlbumRejection,
                rejection.ProposalKey,
                Written(rejection),
                rejection.DecidedBy,
                rejection.RejectedUtc);
        }
    }

    // ----------------------------------------------------------------- shared

    private async Task<Dictionary<AssetKey, int>> AssetRowsAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<int, Guid> sources = await _db.PhotoSources
            .ToDictionaryAsync(source => source.Id, source => source.SharedId, cancellationToken)
            .ConfigureAwait(false);

        var rows = await _db.Assets
            .Select(asset => new { asset.Id, asset.PhotoSourceId, asset.RelativePath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<AssetKey, int> assets = new(rows.Count);

        foreach (var row in rows)
        {
            if (sources.TryGetValue(row.PhotoSourceId, out Guid source))
            {
                assets[new AssetKey(source, row.RelativePath)] = row.Id;
            }
        }

        return assets;
    }

    private async Task<Dictionary<FaceKey, int>> FaceRowsAsync(CancellationToken cancellationToken)
    {
        Dictionary<AssetKey, int> assets =
            await AssetRowsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<int, AssetKey> byRow = assets.ToDictionary(pair => pair.Value, pair => pair.Key);

        var rows = await _db.Faces
            .Select(face => new
            {
                face.Id,
                face.AssetId,
                face.Bounds.X,
                face.Bounds.Y,
                Width = face.Bounds.Width,
                Height = face.Bounds.Height,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<FaceKey, int> faces = new(rows.Count);

        foreach (var row in rows)
        {
            if (byRow.TryGetValue(row.AssetId, out AssetKey photo))
            {
                faces[new FaceKey(photo, new FaceBounds(row.X, row.Y, row.Width, row.Height))] =
                    row.Id;
            }
        }

        return faces;
    }

    /// <summary>An answer as a held row stores it.</summary>
    /// <remarks>
    /// In the same shape a published file uses. A key is a struct with a compact
    /// text form and no parameterless constructor, so the plain serialiser
    /// writes something nothing can read back - which would make every held
    /// answer a silent loss rather than one that waits.
    /// </remarks>
    private static string Written<T>(T answer) =>
        JsonSerializer.Serialize(answer, DecisionSetFile.Shape);

    private sealed record Waiting(
        AssetKey Photo,
        HeldDecisionKind Kind,
        string Part,
        string Payload,
        Guid FromMachine,
        DateTime DecidedUtc);
}
