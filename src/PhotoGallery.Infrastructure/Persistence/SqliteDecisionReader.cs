using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IDecisionReader"/>
/// <remarks>
/// Reads the whole decision set every time, because that is what state-based
/// means: 469 KB on this library, against watermarks that drift and a journal
/// that would have to be compacted.
///
/// <para>Everything is keyed by the source's shared id and the path below it,
/// never by a row id and never by a root - a row id means nothing on another
/// machine, and a root is machine-local text that is a UNC path on one laptop
/// and a drive letter on the next.</para>
/// </remarks>
public sealed class SqliteDecisionReader : IDecisionReader
{
    private readonly GalleryDbContext _db;

    public SqliteDecisionReader(GalleryDbContext db) => _db = db;

    public async Task<DecisionSet> ReadAsync(
        MachineIdentity machine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Dictionary<int, AssetKey> photographs =
            await PhotographsAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<FaceRow> faces = await FaceRowsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<int, FaceKey> faceKeys = [];
        foreach (FaceRow face in faces)
        {
            if (photographs.TryGetValue(face.AssetId, out AssetKey photo))
            {
                faceKeys[face.Id] = new FaceKey(photo, face.Bounds);
            }
        }

        Dictionary<int, Guid> people = await _db.People
            .IgnoreQueryFilters()
            .ToDictionaryAsync(person => person.Id, person => person.PublicId, cancellationToken)
            .ConfigureAwait(false);

        return new DecisionSet(
            machine,
            DateTime.UtcNow,
            await SourcesAsync(cancellationToken).ConfigureAwait(false),
            await PeopleAsync(cancellationToken).ConfigureAwait(false),
            await AnswersAsync(machine, faceKeys, people, cancellationToken).ConfigureAwait(false),
            Strangers(machine, faces, faceKeys),
            await TurnsAsync(machine, photographs, cancellationToken).ConfigureAwait(false),
            await AlbumsAsync(cancellationToken).ConfigureAwait(false),
            await MembershipsAsync(machine, photographs, cancellationToken).ConfigureAwait(false),
            await RejectionsAsync(machine, photographs, cancellationToken).ConfigureAwait(false),
            await ErasAsync(people, cancellationToken).ConfigureAwait(false),
            await LinksAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<LibraryContents> ContentsAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<int, AssetKey> photographs =
            await PhotographsAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<AssetKey, IReadOnlyList<FaceBounds>> faces = [];

        foreach (FaceRow face in await FaceRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!photographs.TryGetValue(face.AssetId, out AssetKey photo))
            {
                continue;
            }

            if (!faces.TryGetValue(photo, out IReadOnlyList<FaceBounds>? boxes))
            {
                faces[photo] = boxes = new List<FaceBounds>();
            }

            ((List<FaceBounds>)boxes).Add(face.Bounds);
        }

        return new LibraryContents(
            new HashSet<Guid>(
                (await SourcesAsync(cancellationToken).ConfigureAwait(false))
                    .Select(source => source.SharedId)),
            new HashSet<AssetKey>(photographs.Values),
            faces);
    }

    public async Task<IReadOnlyList<TurnTarget>> TurnTargetsAsync(
        IReadOnlyList<AssetKey> photographs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photographs);

        if (photographs.Count == 0)
        {
            return [];
        }

        var wanted = new HashSet<AssetKey>(photographs);

        Dictionary<int, Guid> sources =
            await SourceIdsAsync(cancellationToken).ConfigureAwait(false);

        List<TurnTarget> targets = [];

        var rows = await _db.Assets
            .Select(asset => new
            {
                asset.Id,
                asset.PhotoSourceId,
                asset.RelativePath,
                asset.ThumbnailName,
                asset.Rotation,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (sources.TryGetValue(row.PhotoSourceId, out Guid source)
                && wanted.Contains(new AssetKey(source, row.RelativePath)))
            {
                targets.Add(new TurnTarget(
                    new AssetKey(source, row.RelativePath),
                    row.Id,
                    row.ThumbnailName,
                    row.Rotation));
            }
        }

        return targets;
    }

    private async Task<Dictionary<int, Guid>> SourceIdsAsync(CancellationToken cancellationToken) =>
        await _db.PhotoSources
            .ToDictionaryAsync(source => source.Id, source => source.SharedId, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The folders this library holds, by shared id and by the name it calls
    /// them.
    /// </summary>
    /// <remarks>
    /// The count comes with them, as a second signal for the one question the
    /// root is carried to answer: two folders holding sixteen thousand files
    /// each are a likelier pair than one holding sixteen thousand and one
    /// holding nine.
    /// </remarks>
    private async Task<IReadOnlyList<SharedSource>> SourcesAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<int, int> counts = await _db.Assets
            .GroupBy(asset => asset.PhotoSourceId)
            .Select(group => new { Source = group.Key, Held = group.Count() })
            .ToDictionaryAsync(row => row.Source, row => row.Held, cancellationToken)
            .ConfigureAwait(false);

        List<PhotoSource> sources = await _db.PhotoSources
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. sources.Select(source => new SharedSource(
                source.SharedId,
                source.Path,
                counts.TryGetValue(source.Id, out int held) ? held : 0)),
        ];
    }

    private async Task<IReadOnlyList<SourceLink>> LinksAsync(CancellationToken cancellationToken)
    {
        List<PairedSource> pairs = await _db.PairedSources
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. pairs.Select(pair => pair.AsLink())];
    }

    /// <summary>Every indexed file, by row, as the other machines know it.</summary>
    private async Task<Dictionary<int, AssetKey>> PhotographsAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<int, Guid> sources =
            await SourceIdsAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _db.Assets
            .Select(asset => new { asset.Id, asset.PhotoSourceId, asset.RelativePath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, AssetKey> photographs = new(rows.Count);

        foreach (var row in rows)
        {
            // A row whose source has gone is not a photograph any other machine
            // could recognise, so it is left out rather than keyed on nothing.
            if (sources.TryGetValue(row.PhotoSourceId, out Guid source))
            {
                photographs[row.Id] = new AssetKey(source, row.RelativePath);
            }
        }

        return photographs;
    }

    /// <summary>
    /// Every face, without its vector.
    /// </summary>
    /// <remarks>
    /// Deliberately projected rather than loaded as entities: 19,763 faces at two
    /// kilobytes of embedding apiece is 40 MB nothing here looks at.
    /// </remarks>
    private async Task<IReadOnlyList<FaceRow>> FaceRowsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Faces
            .Select(face => new
            {
                face.Id,
                face.AssetId,
                face.Bounds.X,
                face.Bounds.Y,
                Width = face.Bounds.Width,
                Height = face.Bounds.Height,
                face.IgnoredUtc,
                face.IgnoredBy,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new FaceRow(
                row.Id,
                row.AssetId,
                new FaceBounds(row.X, row.Y, row.Width, row.Height),
                row.IgnoredUtc,
                row.IgnoredBy)),
        ];
    }

    /// <summary>Everybody, tombstones included - a deletion is a decision.</summary>
    private async Task<IReadOnlyList<SharedPerson>> PeopleAsync(CancellationToken cancellationToken) =>
        await _db.People
            .IgnoreQueryFilters()
            .Select(person => new SharedPerson(
                person.PublicId,
                person.DisplayName,
                person.BirthYear,
                person.UpdatedUtc,
                person.DeletedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<FaceAnswer>> AnswersAsync(
        MachineIdentity machine,
        Dictionary<int, FaceKey> faces,
        Dictionary<int, Guid> people,
        CancellationToken cancellationToken)
    {
        var rows = await _db.FaceAssignments
            .Select(a => new { a.FaceId, a.PersonId, a.Source, a.DecidedUtc, a.DecidedBy })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(row => faces.ContainsKey(row.FaceId) && people.ContainsKey(row.PersonId))
                .Select(row => new FaceAnswer(
                    faces[row.FaceId],
                    people[row.PersonId],
                    row.Source,
                    row.DecidedUtc,
                    Whose(row.DecidedBy, machine))),
        ];
    }

    private static IReadOnlyList<StrangerFace> Strangers(
        MachineIdentity machine, IReadOnlyList<FaceRow> faces, Dictionary<int, FaceKey> keys) =>
        [
            .. faces
                .Where(face => face.IgnoredUtc is not null && keys.ContainsKey(face.Id))
                .Select(face => new StrangerFace(
                    keys[face.Id], face.IgnoredUtc!.Value, Whose(face.IgnoredBy, machine))),
        ];

    private async Task<IReadOnlyList<PhotoTurn>> TurnsAsync(
        MachineIdentity machine,
        Dictionary<int, AssetKey> photographs,
        CancellationToken cancellationToken)
    {
        // Only pictures somebody actually turned. One nobody has touched is
        // upright as far as this library is concerned and has nothing to say.
        var rows = await _db.Assets
            .Where(asset => asset.RotatedUtc != null)
            .Select(a => new { a.Id, a.Rotation, a.RotatedUtc, a.RotatedBy })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(row => photographs.ContainsKey(row.Id))
                .Select(row => new PhotoTurn(
                    photographs[row.Id],
                    row.Rotation,
                    row.RotatedUtc!.Value,
                    Whose(row.RotatedBy, machine))),
        ];
    }

    /// <summary>Albums, tombstones included, and proposals that carry a decision.</summary>
    private async Task<IReadOnlyList<SharedAlbum>> AlbumsAsync(CancellationToken cancellationToken) =>
        await _db.Collections
            .IgnoreQueryFilters()
            .Select(album => new SharedAlbum(
                album.PublicId,
                album.Name,
                album.Origin,
                album.ProposalKey,
                album.NamedUtc,
                album.DeletedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Which photographs are in which album - for albums somebody made or kept,
    /// and no others.
    /// </summary>
    /// <remarks>
    /// A proposal's contents are derived: the other machine groups the same
    /// photographs into the same days for itself, and better once it has the
    /// confirmations that came with this. Publishing them would put this
    /// library's guesses up against that machine's own rebuild, every time.
    /// </remarks>
    private async Task<IReadOnlyList<AlbumMembership>> MembershipsAsync(
        MachineIdentity machine,
        Dictionary<int, AssetKey> photographs,
        CancellationToken cancellationToken)
    {
        var rows = await _db.CollectionMembers
            .IgnoreQueryFilters()
            .Where(member => member.Collection!.Origin != CollectionOrigin.Proposed
                          && member.Collection.DeletedUtc == null)
            .Select(member => new
            {
                member.AssetId,
                member.Collection!.PublicId,
                member.AddedUtc,
                member.AddedBy,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(row => photographs.ContainsKey(row.AssetId))
                .Select(row => new AlbumMembership(
                    photographs[row.AssetId],
                    row.PublicId,
                    row.AddedUtc,
                    Whose(row.AddedBy, machine))),
        ];
    }

    private async Task<IReadOnlyList<AlbumRejection>> RejectionsAsync(
        MachineIdentity machine,
        Dictionary<int, AssetKey> photographs,
        CancellationToken cancellationToken)
    {
        var rows = await _db.CollectionRejections
            .Select(r => new { r.AssetId, r.ProposalKey, r.RejectedUtc, r.RejectedBy })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(row => photographs.ContainsKey(row.AssetId))
                .Select(row => new AlbumRejection(
                    photographs[row.AssetId],
                    row.ProposalKey,
                    row.RejectedUtc,
                    Whose(row.RejectedBy, machine))),
        ];
    }

    private async Task<IReadOnlyList<SharedEra>> ErasAsync(
        Dictionary<int, Guid> people, CancellationToken cancellationToken)
    {
        List<Domain.People.PersonEra> rows = await _db.PersonEras
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(era => people.ContainsKey(era.PersonId))
                .Select(era => new SharedEra(
                    people[era.PersonId],
                    era.FromUtc,
                    era.ToUtc,
                    era.Centroid,
                    era.SampleCount)),
        ];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Built by reading the whole decision set and keeping the part about these
    /// photographs. Wasteful on paper - the alternative is a second, narrower
    /// projection of every kind of decision - and wrong in practice: two ways of
    /// working out what this library has said about a photograph would agree
    /// today and drift by the third one somebody added. A scan that is deleting
    /// rows can afford one read.
    /// </remarks>
    public async Task<HeldAnswers> AboutAsync(
        IReadOnlyList<int> assetIds,
        MachineIdentity machine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        ArgumentNullException.ThrowIfNull(machine);

        if (assetIds.Count == 0)
        {
            return HeldAnswers.None;
        }

        Dictionary<int, AssetKey> photographs =
            await PhotographsAsync(cancellationToken).ConfigureAwait(false);

        HashSet<AssetKey> leaving =
        [
            .. assetIds
                .Where(photographs.ContainsKey)
                .Select(asset => photographs[asset]),
        ];

        if (leaving.Count == 0)
        {
            return HeldAnswers.None;
        }

        DecisionSet said = await ReadAsync(machine, cancellationToken).ConfigureAwait(false);

        // Proposals are not decisions. A guess parked against a photograph that
        // has gone would come back years later as though somebody had made it.
        return new HeldAnswers(
            [
                .. said.Answers
                    .Where(a => a.Source != AssignmentSource.Proposed
                             && leaving.Contains(a.Face.Photo)),
            ],
            [.. said.Strangers.Where(s => leaving.Contains(s.Face.Photo))],
            [.. said.Turns.Where(t => leaving.Contains(t.Photo))],
            [.. said.Memberships.Where(m => leaving.Contains(m.Photo))],
            [.. said.Rejections.Where(r => leaving.Contains(r.Photo))]);
    }

    /// <inheritdoc/>
    public async Task<PreparedSet> PreparedAsync(
        MachineIdentity machine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Dictionary<int, Guid> sources =
            await SourceIdsAsync(cancellationToken).ConfigureAwait(false);

        // Where each clip's stills were taken from. Not their names: those are
        // seeded from facts the receiving machine's own crawl already has, so it
        // works them out rather than being told.
        Dictionary<int, List<SharedKeyframe>> keyframes = [];

        foreach (var frame in await _db.VideoKeyframes
            .AsNoTracking()
            .Select(frame => new { frame.AssetId, frame.Ordinal, frame.Position })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (!keyframes.TryGetValue(frame.AssetId, out List<SharedKeyframe>? stills))
            {
                keyframes[frame.AssetId] = stills = [];
            }

            stills.Add(new SharedKeyframe(frame.Ordinal, frame.Position));
        }

        var rows = await _db.Assets
            .AsNoTracking()
            // What another machine can actually use: a picture to fetch, or the
            // news that this one will never decode. A row still waiting to be
            // prepared has neither and is nobody else's business.
            .Where(asset => asset.ThumbnailName != null
                         || asset.Status == AssetStatus.Failed
                         || asset.Status == AssetStatus.Skipped)
            .Select(asset => new
            {
                asset.Id,
                asset.PhotoSourceId,
                asset.RelativePath,
                asset.Length,
                asset.ModifiedUtc,
                asset.ContentHash,
                asset.ThumbnailName,
                asset.Width,
                asset.Height,
                asset.TakenUtc,
                asset.Latitude,
                asset.Longitude,
                asset.PerceptualHash,
                asset.Duration,
                asset.Status,
                asset.Rotation,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PreparedFact> facts = new(rows.Count);

        foreach (var row in rows)
        {
            if (!sources.TryGetValue(row.PhotoSourceId, out Guid source))
            {
                continue;
            }

            // A turned photograph is left out entirely. Its renditions were
            // rewritten in place under a name derived from the original's
            // bytes, which the turn did not change - so the name means one
            // thing here and another everywhere else, and cannot be offered.
            // Nor can its facts be offered without it: a fact with no picture
            // tells the other machine the row is settled, and would stop it
            // preparing the very photograph the pool has nothing for.
            if (row.Rotation != 0 && row.ThumbnailName is not null)
            {
                continue;
            }

            facts.Add(new PreparedFact(
                new AssetKey(source, row.RelativePath),
                row.Length,
                row.ModifiedUtc,
                row.ContentHash,
                row.ThumbnailName,
                row.Width ?? 0,
                row.Height ?? 0,
                row.TakenUtc,
                row.Latitude,
                row.Longitude,
                row.PerceptualHash?.ToString(),
                row.Duration,
                row.Status,
                keyframes.TryGetValue(row.Id, out List<SharedKeyframe>? stills)
                    ? [.. stills.OrderBy(still => still.Ordinal)]
                    : []));
        }

        return new PreparedSet(machine, DateTime.UtcNow, facts, new Dictionary<string, string>());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Unprepared>> UnpreparedAsync(
        CancellationToken cancellationToken = default)
    {
        Dictionary<int, Guid> sources =
            await SourceIdsAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _db.Assets
            .AsNoTracking()
            // No rendition yet, whatever the row's status says. Status tracks how
            // the preparing pass got on; the pool cares about one thing, which
            // is whether there is a picture. A row that failed to decode here is
            // very much unprepared - and is exactly the row another machine may
            // have succeeded on.
            .Where(asset => asset.ThumbnailName == null && asset.QuarantinedUtc == null)
            .Select(asset => new
            {
                asset.PhotoSourceId, asset.RelativePath, asset.Length, asset.ModifiedUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Unprepared> waiting = new(rows.Count);

        foreach (var row in rows)
        {
            if (sources.TryGetValue(row.PhotoSourceId, out Guid source))
            {
                waiting.Add(new Unprepared(
                    new AssetKey(source, row.RelativePath), row.Length, row.ModifiedUtc));
            }
        }

        return waiting;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PooledRendition>> RenditionsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Assets
            .AsNoTracking()
            .Where(asset => asset.ThumbnailName != null)
            .Select(asset => new { asset.Id, asset.ThumbnailName, asset.Rotation })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PooledRendition> renditions =
            [.. rows.Select(row => new PooledRendition(row.ThumbnailName!, row.Rotation))];

        // A clip's other frames, which are renditions in the same store under the
        // same rules. Only its poster is on the asset row, so a pool built from
        // the rows alone would offer one frame in four and leave the receiving
        // machine unable to fetch the rest of a video it can already name.
        Dictionary<int, int> rotations = [];
        foreach (var row in rows)
        {
            rotations[row.Id] = row.Rotation;
        }

        var frames = await _db.VideoKeyframes
            .AsNoTracking()
            .Select(frame => new { frame.AssetId, frame.ThumbnailName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var frame in frames)
        {
            renditions.Add(new PooledRendition(
                frame.ThumbnailName,
                rotations.TryGetValue(frame.AssetId, out int turned) ? turned : 0));
        }

        return renditions;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SharedFace>> FacesAsync(
        CancellationToken cancellationToken = default)
    {
        Dictionary<int, AssetKey> photographs =
            await PhotographsAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _db.Faces
            .AsNoTracking()
            .Select(face => new
            {
                face.AssetId, face.Bounds, face.DetectScore, face.Embedding,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<SharedFace> faces = new(rows.Count);

        foreach (var row in rows)
        {
            if (photographs.TryGetValue(row.AssetId, out AssetKey photo))
            {
                faces.Add(new SharedFace(
                    new FaceKey(photo, row.Bounds), row.DetectScore, row.Embedding));
            }
        }

        return faces;
    }

    /// <inheritdoc/>
    public async Task<int> WaitingCountAsync(CancellationToken cancellationToken = default) =>
        await _db.HeldDecisions.CountAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnownMachine>> KnownMachinesAsync(
        CancellationToken cancellationToken = default) =>
        await _db.KnownMachines
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<HeldAnswers> WaitingAsync(CancellationToken cancellationToken = default)
    {
        List<HeldDecision> rows = await _db.HeldDecisions
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<FaceAnswer> answers = [];
        List<StrangerFace> strangers = [];
        List<PhotoTurn> turns = [];
        List<AlbumMembership> memberships = [];
        List<AlbumRejection> rejections = [];

        foreach (HeldDecision row in rows)
        {
            // A row that cannot be read is left where it is rather than thrown
            // away or allowed to stop the sweep. It was written by a version of
            // this app, so the likely reasons are a newer one that wrote a shape
            // this one does not know and a file somebody edited; in both cases
            // the answer is worth more parked than deleted, and the other nine
            // thousand should still land.
            switch (row.Kind)
            {
                case HeldDecisionKind.FaceAnswer:
                    Add(answers, row.Payload);
                    break;

                case HeldDecisionKind.Stranger:
                    Add(strangers, row.Payload);
                    break;

                case HeldDecisionKind.Turn:
                    Add(turns, row.Payload);
                    break;

                case HeldDecisionKind.AlbumMembership:
                    Add(memberships, row.Payload);
                    break;

                case HeldDecisionKind.AlbumRejection:
                    Add(rejections, row.Payload);
                    break;
            }
        }

        return new HeldAnswers(answers, strangers, turns, memberships, rejections);

        static void Add<T>(List<T> into, string payload)
        {
            try
            {
                if (JsonSerializer.Deserialize<T>(payload, DecisionSetFile.Shape) is T answer)
                {
                    into.Add(answer);
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    /// <summary>
    /// Who decided something: the machine recorded against it, or this one where
    /// nothing was.
    /// </summary>
    /// <remarks>
    /// Empty means this library's own answer, written before it had any reason
    /// to say so. Resolving it here rather than at every write is what keeps
    /// naming a face from having to know that sharing exists.
    /// </remarks>
    private static Guid Whose(Guid recorded, MachineIdentity machine) =>
        recorded == Guid.Empty ? machine.Id : recorded;

    private sealed record FaceRow(
        int Id, int AssetId, FaceBounds Bounds, DateTime? IgnoredUtc, Guid IgnoredBy);
}
