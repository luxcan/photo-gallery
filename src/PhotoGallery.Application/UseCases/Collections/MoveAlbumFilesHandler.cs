using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Application.UseCases.Collections;

/// <summary>Moves one album's originals together without changing asset identity.</summary>
public sealed class MoveAlbumFilesHandler
{
    private readonly IAlbumFileMoveRepository _moves;
    private readonly IOriginalFileMover _files;
    private readonly IWorkingFolder _workingFolder;

    public MoveAlbumFilesHandler(
        IAlbumFileMoveRepository moves,
        IOriginalFileMover files,
        IWorkingFolder workingFolder)
    {
        _moves = moves;
        _files = files;
        _workingFolder = workingFolder;
    }

    /// <summary>
    /// Verifies every source file and assigns all conflict-free destination names.
    /// Nothing is changed by this method.
    /// </summary>
    public async Task<AlbumMovePlan> PlanAsync(
        int collectionId,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        AlbumMoveAlbum album = await _moves.FindAlbumAsync(collectionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("That album is no longer in the library.");

        if (album.Origin == CollectionOrigin.Proposed)
        {
            throw new InvalidOperationException(
                "Keep the suggested album before moving its originals.");
        }

        IReadOnlyList<AlbumMoveAsset> assets = await _moves
            .GetAlbumAssetsAsync(collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (assets.Count == 0)
        {
            throw new InvalidOperationException("That album has no originals to move.");
        }

        int[] sourceIds = [.. assets.Select(asset => asset.PhotoSourceId).Distinct()];
        if (sourceIds.Length != 1)
        {
            throw new InvalidOperationException(
                "This album uses more than one photo source. Its originals must stay in their "
                + "current sources for now.");
        }

        string[] roots = [.. assets.Select(asset => FolderOverlap.Normalise(asset.SourceRoot))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (roots.Length != 1)
        {
            throw new InvalidOperationException("The album's photo source changed while it was read.");
        }

        string root = roots[0];
        string destination = FolderOverlap.Normalise(destinationFolder);

        if (!string.Equals(root, destination, StringComparison.OrdinalIgnoreCase)
            && !FolderOverlap.Holds(root, destination))
        {
            throw new InvalidOperationException(
                "Choose a folder inside the album's current photo source. Moving between photo "
                + "sources is not supported yet.");
        }

        if (!_files.DirectoryExists(destination))
        {
            throw new DirectoryNotFoundException("The destination folder cannot be reached.");
        }

        if (_workingFolder.IsAppOwned(destination))
        {
            throw new InvalidOperationException(
                "Choose a photo folder, not Photo Gallery's cache, models, logs, or quarantine.");
        }

        if (_files.HasDirectoryLink(root, destination))
        {
            throw new InvalidOperationException(
                "The destination passes through a linked folder or junction. Choose a regular "
                + "folder inside the photo source so its stored location cannot escape the source.");
        }

        var reserved = new HashSet<string>(
            _files.GetFileNames(destination), StringComparer.OrdinalIgnoreCase);
        var items = new List<AlbumMovePlanItem>(assets.Count);
        int alreadyThere = 0;
        int renamed = 0;
        long totalBytes = 0;

        foreach (AlbumMoveAsset asset in assets
            .OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.AssetId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string sourceFullPath = Below(root, asset.RelativePath);
            OriginalFileSnapshot snapshot = _files.Inspect(sourceFullPath)
                ?? throw new FileNotFoundException(
                    $"The original cannot be found: {Path.GetFileName(asset.RelativePath)}",
                    sourceFullPath);

            if (!Matches(snapshot, asset.Length, asset.ModifiedUtc))
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(asset.RelativePath)} changed since the library last "
                    + "scanned it. Scan the photo source, then try again.");
            }

            string sourceFolder = Path.GetDirectoryName(sourceFullPath) ?? root;
            if (_files.HasDirectoryLink(root, sourceFolder))
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(asset.RelativePath)} is reached through a linked folder "
                    + "or junction. Move it into a regular folder and scan the source first.");
            }

            if (string.Equals(
                FolderOverlap.Normalise(sourceFolder), destination,
                StringComparison.OrdinalIgnoreCase))
            {
                alreadyThere++;
                reserved.Add(Path.GetFileName(sourceFullPath));
                continue;
            }

            string originalName = Path.GetFileName(sourceFullPath);
            string destinationName = AvailableName(originalName, reserved);
            bool wasRenamed = !string.Equals(
                originalName, destinationName, StringComparison.Ordinal);
            string destinationFullPath = Path.Combine(destination, destinationName);
            string destinationRelativePath = Path.GetRelativePath(root, destinationFullPath);

            if (destinationRelativePath.Length > 1024)
            {
                throw new PathTooLongException(
                    $"The destination path for {originalName} is too long for the library index.");
            }

            items.Add(new AlbumMovePlanItem(
                asset.AssetId,
                asset.RelativePath,
                destinationRelativePath,
                sourceFullPath,
                destinationFullPath,
                asset.Length,
                asset.ModifiedUtc,
                wasRenamed));

            totalBytes = checked(totalBytes + asset.Length);
            renamed += wasRenamed ? 1 : 0;
        }

        return new AlbumMovePlan(
            Guid.NewGuid(), album.Id, album.Name, sourceIds[0], root, destination,
            items, alreadyThere, renamed, totalBytes);
    }

    public async Task<AlbumMoveResult> HandleAsync(
        AlbumMovePlan plan,
        IProgress<AlbumMoveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Items.Count == 0)
        {
            return AlbumMoveResult.Nothing(plan.AlreadyThere);
        }

        await _moves.BeginAsync(
            plan.OperationId,
            plan.CollectionId,
            [.. plan.Items.Select(item => new AlbumMoveJournalPlan(
                item.AssetId,
                plan.PhotoSourceId,
                item.SourceRelativePath,
                item.DestinationRelativePath,
                item.ExpectedLength,
                item.ExpectedModifiedUtc))],
            cancellationToken).ConfigureAwait(false);

        AlbumMoveResult executed = await ExecuteAsync(
            plan.OperationId, progress, cancellationToken).ConfigureAwait(false);

        return executed with
        {
            AlreadyThere = plan.AlreadyThere,
            Renamed = executed.Moved == 0 ? 0 : plan.Renamed,
        };
    }

    /// <summary>Finishes journalled operations left by an interrupted app run.</summary>
    public async Task<IReadOnlyList<AlbumMoveResult>> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> operations = await _moves
            .GetActiveOperationsAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<AlbumMoveResult>(operations.Count);

        foreach (Guid operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ExecuteAsync(operation, null, cancellationToken)
                .ConfigureAwait(false));
        }

        return results;
    }

    private async Task<AlbumMoveResult> ExecuteAsync(
        Guid operationId,
        IProgress<AlbumMoveProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Once BeginAsync has committed, cancellation is handled between files
        // below. Always reading the journal here is what lets an immediate Stop
        // close the untouched rows instead of stranding an active operation that
        // can only be recovered by reopening.
        IReadOnlyList<AlbumMoveJournalEntry> entries = await _moves
            .GetOperationAsync(operationId, CancellationToken.None)
            .ConfigureAwait(false);

        int moved = entries.Count(entry => entry.State == AlbumFileMoveState.Completed);
        int failed = entries.Count(entry => entry.State == AlbumFileMoveState.Failed);
        long bytesDone = entries
            .Where(entry => entry.State == AlbumFileMoveState.Completed)
            .Sum(entry => entry.ExpectedLength);
        long totalBytes = entries.Sum(entry => entry.ExpectedLength);
        var errors = new List<string>();
        var attempted = new HashSet<int>();
        bool cancelled = false;

        for (int index = 0; index < entries.Count; index++)
        {
            AlbumMoveJournalEntry entry = entries[index];
            if (entry.State is AlbumFileMoveState.Completed or AlbumFileMoveState.Failed)
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            string fileName = Path.GetFileName(entry.SourceRelativePath);
            attempted.Add(entry.Id);
            progress?.Report(new AlbumMoveProgress(
                fileName, moved + failed, entries.Count, bytesDone, totalBytes));

            string source = Below(entry.SourceRoot, entry.SourceRelativePath);
            string destination = Below(entry.SourceRoot, entry.DestinationRelativePath);
            bool reachedDestination = entry.State == AlbumFileMoveState.FileMoved;

            try
            {
                if (!reachedDestination)
                {
                    OriginalFileSnapshot? atSource = _files.Inspect(source);
                    OriginalFileSnapshot? atDestination = _files.Inspect(destination);

                    if (Matches(atSource, entry.ExpectedLength, entry.ExpectedModifiedUtc)
                        && atDestination is null)
                    {
                        _files.Move(source, destination);
                        reachedDestination = true;
                    }
                    else if (atSource is null
                             && Matches(atDestination, entry.ExpectedLength,
                                 entry.ExpectedModifiedUtc))
                    {
                        // The process stopped after File.Move and before it could
                        // journal that fact. The destination itself is the receipt.
                        reachedDestination = true;
                    }
                    else
                    {
                        throw ReconciliationFailure(fileName, atSource, atDestination, entry);
                    }

                    OriginalFileSnapshot? movedFile = _files.Inspect(destination);
                    if (!Matches(movedFile, entry.ExpectedLength, entry.ExpectedModifiedUtc))
                    {
                        throw new IOException(
                            $"{fileName} did not arrive intact at its destination.");
                    }

                    await _moves.MarkFileMovedAsync(entry.Id, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                OriginalFileSnapshot? settledFile = _files.Inspect(destination);
                if (!Matches(settledFile, entry.ExpectedLength, entry.ExpectedModifiedUtc))
                {
                    throw new IOException(
                        $"{fileName} is no longer intact at its destination.");
                }

                // A stop takes effect between originals, never between a file
                // reaching its destination and the database learning that fact.
                await _moves.CompleteAsync(entry.Id, CancellationToken.None)
                    .ConfigureAwait(false);
                moved++;
                bytesDone += entry.ExpectedLength;
                progress?.Report(new AlbumMoveProgress(
                    Path.GetFileName(entry.DestinationRelativePath),
                    moved + failed, entries.Count, bytesDone, totalBytes));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or InvalidOperationException
                                           or ArgumentException
                                           or NotSupportedException)
            {
                string message = $"{fileName}: {ex.Message}";
                errors.Add(message);
                failed++;

                // Once File.Move has succeeded, the row must stay active. A
                // later recovery can settle the database; marking it failed here
                // would discard the only durable evidence of where it went.
                if (!reachedDestination)
                {
                    await _moves.FailAsync(entry.Id, ex.Message, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }

        if (cancelled)
        {
            // Files not reached are unchanged. Closing their journal rows makes
            // a later press a clean new plan, while a row whose file moved stays
            // active and is recovered before a new library session opens.
            foreach (AlbumMoveJournalEntry remaining in entries
                .Where(entry => entry.State == AlbumFileMoveState.Planned
                                && !attempted.Contains(entry.Id)))
            {
                await _moves.FailAsync(
                    remaining.Id, "Stopped before this file was moved.", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        return new AlbumMoveResult(moved, 0, 0, failed, cancelled, errors);
    }

    private static Exception ReconciliationFailure(
        string fileName,
        OriginalFileSnapshot? source,
        OriginalFileSnapshot? destination,
        AlbumMoveJournalEntry entry)
    {
        if (source is not null && destination is not null)
        {
            return new IOException(
                $"Both the old and new copies of {fileName} exist; neither was changed.");
        }

        if (source is null && destination is null)
        {
            return new FileNotFoundException(
                $"Neither the old nor the new location contains {fileName}.");
        }

        string location = source is not null ? "old" : "new";
        return new IOException(
            $"The file at the {location} location no longer matches the indexed size or date "
            + $"({entry.ExpectedLength:N0} bytes expected); neither location was changed.");
    }

    private static bool Matches(
        OriginalFileSnapshot? snapshot, long expectedLength, DateTime expectedModifiedUtc) =>
        snapshot is not null
        && snapshot.Length == expectedLength
        && Math.Abs((snapshot.ModifiedUtc - expectedModifiedUtc).TotalSeconds) < 2d;

    private static string Below(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("The library contains an absolute asset path.");
        }

        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!FolderOverlap.Holds(root, full))
        {
            throw new InvalidOperationException(
                "An asset path points outside its registered photo source.");
        }

        return full;
    }

    private static string AvailableName(string originalName, HashSet<string> reserved)
    {
        if (reserved.Add(originalName))
        {
            return originalName;
        }

        string stem = Path.GetFileNameWithoutExtension(originalName);
        string extension = Path.GetExtension(originalName);

        for (int number = 2; number < int.MaxValue; number++)
        {
            string candidate = $"{stem} ({number}){extension}";
            if (reserved.Add(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"No unused destination name could be found for {originalName}.");
    }
}
