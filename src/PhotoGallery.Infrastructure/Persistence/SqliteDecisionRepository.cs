using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
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

        progress?.Report(new MergeProgress(
            "Albums", 0, plan.Albums.Count + plan.Moves.Count + plan.DismissedHere.Count));

        // Shelves before the albums that sit on them, so an album arriving on a
        // shelf this library is hearing about in the same breath finds it there.
        // The other order reads every such album as shelfless and drops the one
        // fact the pair of them was carrying.
        int collections =
            await ApplyCollectionsAsync(plan, cancellationToken).ConfigureAwait(false);

        await ApplyDismissalsAsync(plan, cancellationToken).ConfigureAwait(false);

        (int albums, HashSet<int> kept) =
            await ApplyAlbumsAsync(plan, cancellationToken).ConfigureAwait(false);

        (int moved, HashSet<int> settling) =
            await ApplyMovesAsync(plan, cancellationToken).ConfigureAwait(false);

        // A suggestion this merge kept can lose its cover among the photographs
        // let go as it was taken in, so the rule runs over it whether or not
        // anything moved into it.
        settling.UnionWith(kept);

        // After the photographs have moved and been saved, because which picture
        // an album shows is worked out by reading its memberships back out.
        await SettleCoversAsync(plan, settling, cancellationToken).ConfigureAwait(false);

        await ApplyRejectionsAsync(plan, cancellationToken).ConfigureAwait(false);
        await ApplyErasAsync(plan, cancellationToken).ConfigureAwait(false);

        progress?.Report(new MergeProgress("Waiting answers", 0, plan.Held.Count));

        int held = await HoldAsync(plan.Held, cancellationToken).ConfigureAwait(false);

        return new MergeOutcome(
            gained, renamed, deleted, namesGained, namesReplaced, setAside,
            plan.Turns.Count, albums, moved, held,
            plan.Moves, plan.Joins, plan.Refused,
            cancellationToken.IsCancellationRequested,
            collections);
    }

    /// <summary>
    /// Settles the shelves of albums.
    /// </summary>
    /// <remarks>
    /// A collection is created outright where it is not held, rather than
    /// waiting for anything, because there is nothing for it to wait on: it has
    /// no photographs and no rule, so a library that has scanned nothing at all
    /// can still be told what the shelves in this house are called.
    ///
    /// <para>A tombstone takes the albums off the shelf rather than deleting
    /// them. Removing a collection has never removed what was on it - that is
    /// what the screen does too - and a merge that deleted albums because
    /// somebody else tidied a shelf would be the one thing this whole feature
    /// promises it will not do.</para>
    /// </remarks>
    private async Task<int> ApplyCollectionsAsync(
        MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Collections.Count == 0)
        {
            return 0;
        }

        List<Collection> here = await _db.Collections
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // One name per shelf still on the wall is a unique index, so a settled
        // name another live shelf here already holds would fail the whole
        // SaveChanges - taking the albums, the memberships, the eras and the
        // waiting answers of that merge down with it, on this press and on every
        // press afterwards. Every pair of libraries in the house is in exactly
        // that state on the first merge after this release, because collections
        // did not travel and each machine minted its own "Holidays".
        //
        // Numbered rather than refused, the way a second person of one name
        // already is, and kept in step as rows below are tombstoned and renamed.
        var taken = new HashSet<string>(
            here.Where(row => row.DeletedUtc is null).Select(row => row.Name),
            StringComparer.OrdinalIgnoreCase);

        int changed = 0;

        // In one order on every machine. Numbering depends on what has already
        // been taken, so two libraries settling the same pair of shelves in
        // opposite orders would number them differently and then spend every
        // merge afterwards disagreeing about which is which.
        foreach (SharedCollection settled in plan.Collections.OrderBy(row => row.PublicId))
        {
            Collection? collection =
                here.FirstOrDefault(row => row.PublicId == settled.PublicId);

            if (collection is null)
            {
                // One somebody has already taken away is not worth creating in
                // order to record that it is gone: nothing here refers to it.
                if (settled.DeletedUtc is not null)
                {
                    continue;
                }

                string gained = Free(settled.Name, taken);
                taken.Add(gained);

                _db.Collections.Add(new Collection
                {
                    PublicId = settled.PublicId,
                    Name = gained,
                    CreatedUtc = DateTime.UtcNow,
                    NamedUtc = settled.NamedUtc,
                });

                changed++;
                continue;
            }

            bool touched = false;

            if (settled.DeletedUtc != collection.DeletedUtc)
            {
                if (collection.DeletedUtc is null)
                {
                    await _db.Albums
                        .IgnoreQueryFilters()
                        .Where(album => album.CollectionId == collection.Id)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(album => album.CollectionId, (int?)null),
                            cancellationToken)
                        .ConfigureAwait(false);

                    // Out from under the index, so its name is free again.
                    taken.Remove(collection.Name);
                }

                collection.DeletedUtc = settled.DeletedUtc;
                touched = true;
            }

            // The date as well as the text. Settling only the string leaves the
            // two libraries disagreeing about when it was typed, which is what
            // the next disagreement would have been judged on - and leaves this
            // shelf in every plan from now on, so merging twice stops changing
            // nothing.
            if (!string.Equals(collection.Name, settled.Name, StringComparison.Ordinal)
                || collection.NamedUtc != settled.NamedUtc)
            {
                if (collection.DeletedUtc is null)
                {
                    taken.Remove(collection.Name);
                    string free = Free(settled.Name, taken);
                    taken.Add(free);
                    collection.Name = free;
                }
                else
                {
                    // Under a tombstone it is outside the index, and takes the
                    // settled name as it stands.
                    collection.Name = settled.Name;
                }

                collection.NamedUtc = settled.NamedUtc;
                touched = true;
            }

            // One shelf changed is one shelf, whether this merge renamed it,
            // removed it, or both.
            if (touched)
            {
                changed++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return changed;
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

            // Two folders can hold the same path - a phone dump beside the folder
            // its photographs were copied into is the ordinary way it happens -
            // so once they are one folder the two answers about IMG_0001.jpg are
            // one answer, which the unique index will not let them become on
            // their own. The duplicate goes first: both rows say the same thing
            // about the same photograph, and the alternative is the rename
            // throwing and taking the whole pairing down with it.
            await _db.HeldDecisions
                .Where(held => held.SharedSourceId == from
                    && _db.HeldDecisions.Any(kept =>
                        kept.SharedSourceId == to
                        && kept.RelativePath == held.RelativePath
                        && kept.Kind == held.Kind
                        && kept.Part == held.Part))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            // Answers parked under the old identity are about the same
            // photographs; left behind, they would wait for a folder that no
            // longer exists under that name.
            List<HeldDecision> moving = await _db.HeldDecisions
                .Where(held => held.SharedSourceId == from)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (HeldDecision held in moving)
            {
                // The answer inside as well as the row around it, because the
                // answer is what the sweep reads. One row at a time, and only
                // where the answer read back names the old folder: a row this
                // version cannot make sense of keeps what it says, and still
                // moves with the folder.
                HeldAnswers answer = HeldRows.Read([held]);

                if (answer.Photographs.Any(photo => photo.SharedSourceId == from)
                    && Flatten(answer.Renamed(from, to)).SingleOrDefault() is Waiting renamed)
                {
                    held.Payload = renamed.Payload;
                }

                held.SharedSourceId = to;
            }

            // Saved before the next rename looks for rows, as the statement
            // this replaced was: the next one asks the database, not the
            // change tracker.
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

        KnownMachine? known = await _db.KnownMachines
            .FirstOrDefaultAsync(row => row.MachineId == machine.Id, cancellationToken)
            .ConfigureAwait(false);

        if (known is null)
        {
            _db.KnownMachines.Add(new KnownMachine
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
            known.Name = machine.Name;
            known.LastMergedUtc = mergedUtc;
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
    private static string Free(string wanted, IEnumerable<Person> here) =>
        Free(
            wanted,
            new HashSet<string>(
                here.Where(person => person.DeletedUtc is null)
                    .Select(person => person.DisplayName),
                StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc cref="Free(string, IEnumerable{Person})"/>
    /// <remarks>
    /// The same numbering over a set of names somebody else has gathered, so
    /// that shelves can borrow it. A merge settling several of them has to keep
    /// its own set in step as it goes - the rows it has already added are not in
    /// the database yet, and a second one would take the same name.
    /// </remarks>
    private static string Free(string wanted, ISet<string> taken)
    {
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

    /// <returns>
    /// How many albums changed, and the rows of the suggestions this merge
    /// kept - each of which needs its cover worked out again.
    /// </returns>
    private async Task<(int Changed, HashSet<int> Kept)> ApplyAlbumsAsync(
        MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Albums.Count == 0 && plan.DismissedHere.Count == 0)
        {
            return (0, []);
        }

        List<Album> here = await _db.Albums
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int changed = await TakeAwayAsync(plan.DismissedHere, here, cancellationToken)
            .ConfigureAwait(false);

        HashSet<int> kept = [];

        // Every refusal this merge leaves standing, read once and only if a
        // suggestion is kept - which most merges keep none of.
        Dictionary<string, HashSet<int>>? refusals = null;

        async Task KeepAsync(Album album, DateTime? keptUtc)
        {
            refusals ??= await RefusalsAsync(plan, cancellationToken).ConfigureAwait(false);

            await KeepHereAsync(
                album,
                keptUtc,
                refusals.GetValueOrDefault(album.ProposalKey ?? string.Empty) ?? [],
                cancellationToken).ConfigureAwait(false);

            kept.Add(album.Id);
        }

        // Written by the pass just above this one, so a shelf arriving in the
        // same merge is already here to be found.
        Dictionary<Guid, int> shelves = await _db.Collections
            .IgnoreQueryFilters()
            .Where(collection => collection.DeletedUtc == null)
            .ToDictionaryAsync(
                collection => collection.PublicId,
                collection => collection.Id,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (SharedAlbum settled in plan.Albums)
        {
            Album? album = Match(here, settled);

            if (album is null)
            {
                // One already taken away is written as a tombstone rather than
                // skipped, which is the opposite of what a shelf does and for a
                // reason a shelf has not got: photographs point at albums. With
                // no row, this library cannot tell "deleted" from "never heard
                // of", so a third machine that still holds the album goes on
                // offering its memberships, they cannot be placed, and every
                // merge from now on plans the same moves and applies none of
                // them - "Nothing new" on screen, for ever. The row is invisible
                // to every query in the app and is what lets the next merge
                // settle.
                //
                // A kept suggestion's tombstone keeps its run of days, which
                // used to be the reason it was not written: its key is what a
                // rebuild matches on, so it would be a second row answering to
                // days this machine was about to group again. That is now what
                // it is for. Deleting a kept suggestion means "never suggest
                // these days again", the rebuild reads a tombstone's key as that
                // answer, and without the row this machine's next scan offered
                // the very days somebody had thrown away. No row here holds the
                // key, or the match above would have found it, so the unique
                // index on it has nothing to refuse.
                //
                // Whatever it was before it went, and that is why this comes
                // before the rule about suggestions below. A suggestion arriving
                // as a tombstone is another machine's own suggestion for days
                // whose kept suggestion somebody deleted - the deletion reached
                // it there first and turned it into one. Nothing else makes one:
                // a rebuild deletes the suggestions it drops outright, and so
                // does dismissing one. So it is the same answer about the same
                // days. Which of the two this library settles on follows the
                // name, and the origin rides along with it, so skipping it as a
                // suggestion left the deleted days remembered here or offered
                // again depending on who had renamed what.
                if (settled.DeletedUtc is not null)
                {
                    _db.Albums.Add(new Album
                    {
                        PublicId = settled.PublicId,
                        Name = settled.Name,
                        StartUtc = DateTime.UtcNow,
                        EndUtc = DateTime.UtcNow,
                        Kind = AlbumKind.Period,
                        Origin = settled.Origin,
                        ProposalKey = settled.ProposalKey,
                        NamedUtc = settled.NamedUtc,
                        KeptUtc = settled.KeptUtc,
                        DeletedUtc = settled.DeletedUtc,
                        BuiltUtc = DateTime.UtcNow,
                    });

                    changed++;
                    continue;
                }

                // Only an album somebody made or kept is created from a merge. A
                // live proposal is derived, so its row is this machine's own to
                // build - what travels about one is a name, and a name with
                // nothing to sit on waits for the rebuild that makes it. The
                // merge asks the same question before planning one at all, so
                // this is a guard rather than a decision.
                if (!DecisionMerge.CreatesARow(settled))
                {
                    continue;
                }

                // Kept elsewhere at the moment it was kept there, not the moment
                // it arrived here: a dismissal made in between has to be able
                // to tell which came last.
                _db.Albums.Add(new Album
                {
                    PublicId = settled.PublicId,
                    Name = settled.Name,
                    StartUtc = DateTime.UtcNow,
                    EndUtc = DateTime.UtcNow,
                    Kind = AlbumKind.Period,
                    Origin = settled.Origin,
                    ProposalKey = settled.ProposalKey,
                    NamedUtc = settled.NamedUtc,
                    KeptUtc = settled.KeptUtc,
                    BuiltUtc = DateTime.UtcNow,
                    CollectionId = Shelf(shelves, settled),
                    ShelvedUtc = settled.ShelvedUtc,
                });

                changed++;
                continue;
            }

            // One album changed is one album, whether this merge renamed it,
            // removed it, moved it to a shelf, or all three. Counting the
            // branches instead reports three albums to somebody who has one.
            bool touched = false;

            // The row keeps its run of days when it goes, whatever it was here.
            // This machine's own suggestion for days somebody else kept and then
            // deleted lands here too, and the key it keeps is how its next scan
            // knows not to offer those days again - the photographs let go below
            // are exactly the ones that would.
            if (settled.DeletedUtc is not null && album.DeletedUtc is null)
            {
                await _db.AlbumMembers
                    .Where(member => member.AlbumId == album.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                album.DeletedUtc = settled.DeletedUtc;
                touched = true;
            }
            else if (settled.DeletedUtc < album.DeletedUtc)
            {
                // Two tombstones, and the merge settled on the earlier moment.
                // Nobody sees which, so it is not counted, but left behind it
                // keeps the album in every plan from here on.
                album.DeletedUtc = settled.DeletedUtc;
            }

            // The name and its date together, whenever either differs. Written
            // only when the name itself changed, a name typed on another laptop
            // to the very words this one already showed never took that
            // laptop's date - and the album was planned again on every merge
            // afterwards, for a change nobody could see. Counted only when the
            // name is different, because that is the one somebody would notice.
            bool renamed = !string.Equals(album.Name, settled.Name, StringComparison.Ordinal);

            if (renamed || album.NamedUtc != settled.NamedUtc)
            {
                album.Name = settled.Name;
                album.NamedUtc = settled.NamedUtc;
                touched |= renamed;
            }

            // Kept on another laptop, where this one still only suggests those
            // days: kept here too, where it stands. The row keeps its own
            // identity and its place on the wall, and from now on the rebuild
            // leaves it alone, the album picker offers it, and its photographs
            // are published. Never the other way: nothing a merge carries turns
            // a kept album back into a suggestion.
            if (settled.Origin == AlbumOrigin.Accepted
                && album.Origin == AlbumOrigin.Proposed
                && settled.DeletedUtc is null
                && album.DeletedUtc is null)
            {
                await KeepAsync(album, settled.KeptUtc).ConfigureAwait(false);
                touched = true;
            }
            else if (settled.Origin == AlbumOrigin.Accepted
                && album.Origin == AlbumOrigin.Accepted
                && settled.DeletedUtc is null
                && album.DeletedUtc is null
                && album.KeptUtc != settled.KeptUtc)
            {
                // The latest keep anybody can date, which is what the next
                // dismissal of these days is judged against. Not counted:
                // nothing about the album that anybody sees has changed.
                album.KeptUtc = settled.KeptUtc;
            }

            // Compared against the row rather than trusted from the plan: the
            // plan carries an album because something about it differs, and the
            // shelf is often not the thing that did.
            int? shelf = Shelf(shelves, settled);

            // The date settles even where the shelf already agrees. It is what
            // the next disagreement about this album is judged on, and leaving
            // it behind keeps the album in every plan from here on - which is
            // "merging twice changes nothing" quietly ceasing to be true.
            if (album.CollectionId != shelf || album.ShelvedUtc != settled.ShelvedUtc)
            {
                // Putting a suggestion on a shelf is keeping it - the same
                // decision the tick list makes, and for the same reason. A
                // proposal is still a question as far as a rebuild is concerned,
                // and a rebuild removes a question nobody answered, taking the
                // album off the shelf somebody just filled with it. Only a
                // suggestion shelved by a release before shelving kept one
                // reaches this; a keep arriving with its shelf was kept above,
                // and the shelving was the keep, so it carries that moment.
                if (shelf is not null && album.Origin == AlbumOrigin.Proposed)
                {
                    await KeepAsync(album, settled.ShelvedUtc).ConfigureAwait(false);
                }

                album.CollectionId = shelf;
                album.ShelvedUtc = settled.ShelvedUtc;
                touched = true;
            }

            if (touched)
            {
                changed++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return (changed, kept);
    }

    /// <summary>
    /// Takes away this library's own kept albums that a later dismissal beat.
    /// </summary>
    /// <remarks>
    /// Deleted outright, with the photographs' places in them, and leaving no
    /// tombstone. A tombstone keeping these days would be read by the rebuild
    /// as "never suggest these days again", which is what deleting a kept album
    /// says and not what dismissing a suggestion says - and no later keep could
    /// ever bring the album back. The photographs themselves stay in the
    /// library.
    ///
    /// <para>No refusals are written. The dismisser's own arrive in this same
    /// merge, and writing more would put words in somebody's mouth about
    /// photographs they never looked at.</para>
    ///
    /// <para>Moves waiting here for a photograph to arrive into one of these
    /// albums go with it. Nothing will ever place them once the album is gone,
    /// and kept, they would sit on the Sync screen as waiting for good.</para>
    ///
    /// <para>Saved before anything else is written, for the reason the rebuild
    /// saves its removals first: a photograph has one membership row, and a move
    /// filing it elsewhere in this same merge must not meet the old one.</para>
    /// </remarks>
    private async Task<int> TakeAwayAsync(
        IReadOnlyList<SharedAlbumDismissal> dismissedHere,
        List<Album> here,
        CancellationToken cancellationToken)
    {
        int taken = 0;

        foreach (SharedAlbumDismissal dismissal in dismissedHere)
        {
            Album? kept = here.FirstOrDefault(row =>
                row.ProposalKey == dismissal.ProposalKey
                && row.DeletedUtc == null
                && row.Origin == AlbumOrigin.Accepted);

            if (kept is null)
            {
                continue;
            }

            string waitingFor = PartOf(kept.PublicId);

            await _db.HeldDecisions
                .Where(held => held.Kind == HeldDecisionKind.SharedAlbumMembership
                            && held.Part == waitingFor)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            _db.Albums.Remove(kept);
            here.Remove(kept);
            taken++;
        }

        if (taken > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return taken;
    }

    /// <summary>
    /// Keeps this library's own suggestion for days somebody kept on another
    /// laptop, where it stands.
    /// </summary>
    /// <remarks>
    /// Until now the rebuild owned everything in it, and everything in it the
    /// rebuild put there carries no author. From now on it is published as this
    /// library's answer, so two things about it change first.
    ///
    /// <para>What the rebuild put there that somebody refused for these days
    /// goes. Taken out of the kept album on the other laptop, a photograph is
    /// remembered only as a refusal - and published here as this library's
    /// answer, newer than anything said there, it would go straight back into
    /// the album it was taken out of. Only what the rebuild put there: a
    /// photograph somebody put back after taking it out keeps its place,
    /// because putting it back never clears the refusal.</para>
    ///
    /// <para>What the rebuild put there that stays is dated
    /// <see cref="AlbumMember.AppPlacedUtc"/>, the earliest moment there is. It
    /// was dated whenever the last scan ran, and published at that date it
    /// would beat somebody on another laptop who filed the photograph elsewhere
    /// days before. Nobody chose this one, so anything a person chose beats
    /// it.</para>
    ///
    /// <para>Before the moves of the same merge, which then rewrite every
    /// photograph the other laptop's album holds with that laptop's moment and
    /// author.</para>
    /// </remarks>
    /// <param name="refused">
    /// The photographs refused for these days, here and in this merge's plan -
    /// the plan's are written later in the merge, and are wanted now.
    /// </param>
    private async Task KeepHereAsync(
        Album album,
        DateTime? keptUtc,
        IReadOnlySet<int> refused,
        CancellationToken cancellationToken)
    {
        int[] unwanted = [.. refused];

        await _db.AlbumMembers
            .Where(member => member.AlbumId == album.Id
                          && member.AddedBy == Guid.Empty
                          && unwanted.Contains(member.AssetId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await _db.AlbumMembers
            .Where(member => member.AlbumId == album.Id && member.AddedBy == Guid.Empty)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(member => member.AddedUtc, AlbumMember.AppPlacedUtc),
                cancellationToken)
            .ConfigureAwait(false);

        album.Keep(keptUtc);
    }

    /// <summary>
    /// Every photograph refused for each run of days, as it will stand by the
    /// end of this merge: what this library holds, and what the plan is about
    /// to add.
    /// </summary>
    private async Task<Dictionary<string, HashSet<int>>> RefusalsAsync(
        MergePlan plan, CancellationToken cancellationToken)
    {
        var held = await _db.AlbumRejections
            .AsNoTracking()
            .Select(rejection => new { rejection.AssetId, rejection.ProposalKey })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<AssetKey, int> assets =
            await AssetRowsAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<(int AssetId, string ProposalKey)> arriving = plan.Rejections
            .Where(rejection => assets.ContainsKey(rejection.Photo))
            .Select(rejection => (assets[rejection.Photo], rejection.ProposalKey));

        Dictionary<string, HashSet<int>> refused = new(StringComparer.Ordinal);

        foreach ((int assetId, string days) in
                 held.Select(row => (row.AssetId, row.ProposalKey)).Concat(arriving))
        {
            if (!refused.TryGetValue(days, out HashSet<int>? photographs))
            {
                refused[days] = photographs = [];
            }

            photographs.Add(assetId);
        }

        return refused;
    }

    /// <summary>
    /// Remembers the latest dismissal of each run of days the merge settled on.
    /// </summary>
    /// <remarks>
    /// The merge has already chosen the winner, so this only writes it, author
    /// and moment as they arrived. Before the albums, so that a merge stopped
    /// between the two still finds the dismissal standing next time and takes
    /// the kept album away then. Not counted: it changes nothing anybody sees,
    /// only what this library tells the next one.
    /// </remarks>
    private async Task ApplyDismissalsAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Dismissals.Count == 0)
        {
            return;
        }

        Dictionary<string, AlbumDismissal> here = await _db.AlbumDismissals
            .ToDictionaryAsync(row => row.ProposalKey, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        foreach (SharedAlbumDismissal dismissal in plan.Dismissals)
        {
            if (!here.TryGetValue(dismissal.ProposalKey, out AlbumDismissal? row))
            {
                row = new AlbumDismissal { ProposalKey = dismissal.ProposalKey };
                _db.AlbumDismissals.Add(row);
                here[dismissal.ProposalKey] = row;
            }

            row.DismissedUtc = dismissal.DismissedUtc;
            row.DismissedBy = dismissal.DecidedBy;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    /// <summary>
    /// The row number of the shelf an album settled onto, or null for none.
    /// </summary>
    /// <remarks>
    /// A shelf this library has never been told about, or one it has a tombstone
    /// for, reads as no shelf. That is what the wall already does with an
    /// unknown identity, and it is the only answer that cannot invent a shelf
    /// nobody made.
    /// </remarks>
    private static int? Shelf(Dictionary<Guid, int> shelves, SharedAlbum album) =>
        album.Shelf is Guid publicId && shelves.TryGetValue(publicId, out int id) ? id : null;

    /// <summary>
    /// The row an album names: by its run of days where it has one, and by its
    /// identity otherwise.
    /// </summary>
    private static Album? Match(List<Album> here, SharedAlbum album) =>
        album.ProposalKey is null
            ? here.FirstOrDefault(row => row.PublicId == album.PublicId)
            : here.FirstOrDefault(row => row.ProposalKey == album.ProposalKey)
              ?? here.FirstOrDefault(row => row.PublicId == album.PublicId);

    private async Task<(int Moved, HashSet<int> Settling)> ApplyMovesAsync(
        MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Moves.Count == 0)
        {
            return (0, []);
        }

        Dictionary<AssetKey, int> assets =
            await AssetRowsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, int> albums = await _db.Albums
            .IgnoreQueryFilters()
            .Where(album => album.DeletedUtc == null)
            .ToDictionaryAsync(album => album.PublicId, album => album.Id, cancellationToken)
            .ConfigureAwait(false);

        // Where these photographs are now, asked before any of them moves. An
        // album a photograph leaves has to be settled as much as the one it
        // joins - it may have been showing the picture that just left - and
        // after the delete below there is nothing left to ask. Read in one
        // query rather than one per move, because a merge that fills a holiday
        // album moves a thousand photographs and they are nearly all in the
        // same two or three albums.
        HashSet<int> moving =
        [
            .. plan.Moves
                .Where(move => assets.ContainsKey(move.Photo))
                .Select(move => assets[move.Photo]),
        ];

        HashSet<int> settling =
        [
            .. await _db.AlbumMembers
                .AsNoTracking()
                .Where(member => moving.Contains(member.AssetId))
                .Select(member => member.AlbumId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
        ];

        int moved = 0;

        foreach (SharedAlbumMove move in plan.Moves)
        {
            // A guard rather than a decision, and the difference matters. This
            // used to be where a membership quietly died: an album is named by
            // the identity the machine that published it uses, a proposal's
            // identity is different on every machine, and the lookup simply
            // failed - so somebody's tidying was dropped with no row, no
            // waiting answer and no count, and re-proposed on every merge
            // afterwards. The merge now settles what an album is called here
            // and refuses what cannot land, so nothing that reaches this loop
            // should fail to place. See DecisionMerge's album resolution.
            if (!assets.TryGetValue(move.Photo, out int assetId)
                || !albums.TryGetValue(move.To, out int albumId))
            {
                continue;
            }

            settling.Add(albumId);

            // One delete and one insert, because a photograph's row is its whole
            // primary key in that table - the schema refuses a second rather
            // than overwriting it.
            await _db.AlbumMembers
                .Where(member => member.AssetId == assetId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            _db.AlbumMembers.Add(new AlbumMember
            {
                AssetId = assetId,
                AlbumId = albumId,
                AddedUtc = move.AddedUtc,
                AddedBy = move.DecidedBy,
            });

            moved++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return (moved, settling);
    }

    /// <summary>
    /// Puts the right picture on every album this merge touched: the one
    /// somebody chose where a choice arrived, and the rule everywhere else.
    /// </summary>
    /// <remarks>
    /// The half of an add the merge was not doing. Every local way into an album
    /// ends at <see cref="AlbumCovers"/>, and the merge writes its memberships
    /// itself - so an album that arrived from another machine was created with
    /// no cover and then filled with photographs by a path that never worked one
    /// out. It stood on the wall as a grey square for ever, because no later
    /// pass repairs it: the rebuild only touches the albums it proposed itself.
    ///
    /// <para>Last of the album passes, and that is the whole design. A cover
    /// naming a photograph is a claim about a membership, and the memberships
    /// arrive in the same merge - so the choice can only be checked once they
    /// have landed and been saved. Checking it earlier would refuse every cover
    /// that arrived with its own album.</para>
    ///
    /// <para>Written only where it differs, so merging twice changes nothing.
    /// And the rule runs afterwards either way: it leaves a chosen cover exactly
    /// where it is, and catches the one case a choice cannot cover - a
    /// photograph this library has, but which its own copy of the album does not
    /// hold.</para>
    /// </remarks>
    private async Task SettleCoversAsync(
        MergePlan plan, HashSet<int> settling, CancellationToken cancellationToken)
    {
        await ApplyChosenCoversAsync(plan, settling, cancellationToken).ConfigureAwait(false);

        if (settling.Count == 0)
        {
            return;
        }

        foreach (int albumId in settling)
        {
            await AlbumCovers.EnsureAsync(_db, albumId, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    /// <summary>Takes the covers somebody on another machine chose.</summary>
    /// <remarks>
    /// The album is named the way every other album pass names it, by its run of
    /// days where it has one - see <see cref="Match"/> - and the photograph by
    /// the key the two libraries share. A cover this library cannot place is
    /// left alone rather than cleared: the merge only proposes one for a
    /// photograph this machine has indexed, and what is left after that is an
    /// album whose copy here does not hold the picture, which is a disagreement
    /// about memberships rather than about covers.
    /// </remarks>
    private async Task ApplyChosenCoversAsync(
        MergePlan plan, HashSet<int> settling, CancellationToken cancellationToken)
    {
        List<SharedAlbum> chosen =
            [.. plan.Albums.Where(album => album.Cover is not null && album.DeletedUtc is null)];

        if (chosen.Count == 0)
        {
            return;
        }

        Dictionary<AssetKey, int> assets =
            await AssetRowsAsync(cancellationToken).ConfigureAwait(false);

        List<Album> here = await _db.Albums
            .IgnoreQueryFilters()
            .Where(album => album.DeletedUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (SharedAlbum settled in chosen)
        {
            if (Match(here, settled) is not Album album
                || !assets.TryGetValue(settled.Cover!.Value, out int assetId))
            {
                continue;
            }

            // The rule below runs over this album either way, which is what
            // turns a choice this library cannot honour back into a picture it
            // can - so it is added whether or not the choice is written.
            settling.Add(album.Id);

            if (album.CoverAssetId == assetId && album.CoverChosenUtc == settled.CoverChosenUtc)
            {
                continue;
            }

            album.CoverAssetId = assetId;
            album.CoverChosenUtc = settled.CoverChosenUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
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
            .. await _db.AlbumRejections
                .Select(r => ValueTuple.Create(r.AssetId, r.ProposalKey))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
        ];

        foreach (SharedAlbumRejection rejection in plan.Rejections)
        {
            if (assets.TryGetValue(rejection.Photo, out int assetId)
                && here.Add((assetId, rejection.ProposalKey)))
            {
                _db.AlbumRejections.Add(new AlbumRejection
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
    public async Task<int> FillInAsync(
        IReadOnlyList<PreparedFact> facts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Count == 0)
        {
            return 0;
        }

        Dictionary<AssetKey, int> rows =
            await AssetRowsAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<AssetKey, PreparedFact> byPhoto = [];
        foreach (PreparedFact fact in facts)
        {
            byPhoto[fact.Photo] = fact;
        }

        int[] wanted = [.. byPhoto.Keys.Where(rows.ContainsKey).Select(photo => rows[photo])];

        if (wanted.Length == 0)
        {
            return 0;
        }

        List<Asset> assets = await _db.Assets
            .Where(asset => wanted.Contains(asset.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, AssetKey> keys = [];
        foreach ((AssetKey photo, int row) in rows)
        {
            keys[row] = photo;
        }

        int filled = 0;

        foreach (Asset asset in assets)
        {
            if (!keys.TryGetValue(asset.Id, out AssetKey photo)
                || !byPhoto.TryGetValue(photo, out PreparedFact? fact))
            {
                continue;
            }

            // Checked again here, against the row rather than against the plan.
            // The plan was made from a read that has since been through a file
            // copy taking minutes, and the one thing that must never happen is a
            // rendition of one set of bytes recorded against another.
            if (!fact.Describes(asset.Length, asset.ModifiedUtc))
            {
                continue;
            }

            // Already exactly this. Skipped rather than rewritten, because a
            // fact that lands twice would be reported as work done twice -
            // and the pool's whole claim is that running it again copies
            // nothing.
            if (asset.ThumbnailName == fact.ThumbnailName && asset.Status == fact.Status)
            {
                continue;
            }

            asset.ContentHash = fact.ContentHash;
            asset.ThumbnailName = fact.ThumbnailName;
            asset.Width = fact.Width == 0 ? asset.Width : fact.Width;
            asset.Height = fact.Height == 0 ? asset.Height : fact.Height;
            asset.TakenUtc = fact.TakenUtc;
            asset.Latitude = fact.Latitude;
            asset.Longitude = fact.Longitude;
            asset.Duration = fact.Duration;
            asset.Status = fact.Status;

            if (PerceptualHash.TryParse(fact.PerceptualHash, out PerceptualHash hash))
            {
                asset.PerceptualHash = hash;
            }

            Frames(asset, fact);

            filled++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return filled;
    }

    /// <summary>
    /// Writes a clip's frame rows, with the names this machine works out for
    /// itself.
    /// </summary>
    /// <remarks>
    /// The rows come from the manifest and the names do not. A frame's name is
    /// seeded from the path, the length, the modified time and the ordinal, all
    /// of which this library's own crawl already collected - so being told them
    /// would be carrying 4,743 clips' worth of strings that can be derived in a
    /// millisecond, and would break the moment one machine's copy of a video
    /// differed.
    /// </remarks>
    private void Frames(Asset asset, PreparedFact fact)
    {
        if (fact.Keyframes.Count == 0)
        {
            return;
        }

        HashSet<int> here =
        [
            .. _db.VideoKeyframes
                .Where(frame => frame.AssetId == asset.Id)
                .Select(frame => frame.Ordinal),
        ];

        foreach (SharedKeyframe still in fact.Keyframes)
        {
            if (!here.Add(still.Ordinal))
            {
                continue;
            }

            _db.VideoKeyframes.Add(new VideoKeyframe
            {
                AssetId = asset.Id,
                Ordinal = still.Ordinal,
                Position = still.Position,
                ThumbnailName = RenditionName.For(VideoKeyframeIdentity.For(
                    asset.RelativePath, asset.Length, asset.ModifiedUtc, still.Ordinal)),
            });
        }
    }

    /// <inheritdoc/>
    public async Task<int> AddFacesAsync(
        IReadOnlyList<SharedFace> faces, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faces);

        if (faces.Count == 0)
        {
            return 0;
        }

        Dictionary<AssetKey, int> rows =
            await AssetRowsAsync(cancellationToken).ConfigureAwait(false);

        HashSet<int> looked = [];
        int added = 0;

        foreach (SharedFace face in faces)
        {
            if (!rows.TryGetValue(face.Face.Photo, out int asset))
            {
                continue;
            }

            _db.Faces.Add(new Face
            {
                AssetId = asset,
                Bounds = face.Face.Bounds,
                DetectScore = face.DetectScore,
                Embedding = face.Embedding,
            });

            looked.Add(asset);
            added++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Stamped with them, and this is not decoration: the detection pass
        // selects on it, so a row left null would be read and detected again on
        // the next scan - the whole two hours coming back, having just been
        // avoided.
        foreach (int[] chunk in looked.Chunk(400))
        {
            await _db.Assets
                .Where(asset => chunk.Contains(asset.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(asset => asset.FacesDetectedUtc, DateTime.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        _db.ChangeTracker.Clear();
        return added;
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

        foreach (SharedAlbumMembership membership in held.Memberships)
        {
            yield return new Waiting(
                membership.Photo,
                HeldDecisionKind.SharedAlbumMembership,
                PartOf(membership.Album),
                Written(membership),
                membership.DecidedBy,
                membership.AddedUtc);
        }

        foreach (SharedAlbumRejection rejection in held.Rejections)
        {
            yield return new Waiting(
                rejection.Photo,
                HeldDecisionKind.SharedAlbumRejection,
                rejection.ProposalKey,
                Written(rejection),
                rejection.DecidedBy,
                rejection.RejectedUtc);
        }
    }

    /// <summary>
    /// The part a move waiting for its photograph is filed under: the album it
    /// is going into.
    /// </summary>
    /// <remarks>
    /// One spelling for the two places that need it - parking a move, and
    /// letting go of the moves into an album a dismissal took away. Spelt twice,
    /// the two would drift, and moves into a removed album would wait for ever.
    /// </remarks>
    private static string PartOf(Guid album) => album.ToString("D");

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
