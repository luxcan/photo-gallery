using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IAlbumFileMoveRepository"/>
public sealed class SqliteAlbumFileMoveRepository : IAlbumFileMoveRepository
{
    private readonly GalleryDbContext _db;

    public SqliteAlbumFileMoveRepository(GalleryDbContext db) => _db = db;

    public Task<AlbumMoveAlbum?> FindAlbumAsync(
        int collectionId, CancellationToken cancellationToken = default) =>
        _db.Collections
            .AsNoTracking()
            .Where(collection => collection.Id == collectionId)
            .Select(collection => new AlbumMoveAlbum(
                collection.Id,
                collection.Name,
                collection.Origin,
                collection.Members.Count))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AlbumMoveAsset>> GetAlbumAssetsAsync(
        int collectionId, CancellationToken cancellationToken = default)
    {
        return await _db.CollectionMembers
            .AsNoTracking()
            .Where(member => member.CollectionId == collectionId)
            .Join(
                _db.Assets.AsNoTracking(),
                member => member.AssetId,
                asset => asset.Id,
                (member, asset) => asset)
            .Join(
                _db.PhotoSources.AsNoTracking(),
                asset => asset.PhotoSourceId,
                source => source.Id,
                (asset, source) => new { Asset = asset, SourceRoot = source.Path })
            .OrderBy(row => row.Asset.RelativePath)
            .ThenBy(row => row.Asset.Id)
            .Select(row => new AlbumMoveAsset(
                row.Asset.Id,
                row.Asset.PhotoSourceId,
                row.SourceRoot,
                row.Asset.RelativePath,
                row.Asset.Length,
                row.Asset.ModifiedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task BeginAsync(
        Guid operationId,
        int collectionId,
        IReadOnlyList<AlbumMoveJournalPlan> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            return;
        }

        if (await _db.AlbumFileMoves.AnyAsync(
            move => move.OperationId == operationId, cancellationToken).ConfigureAwait(false))
        {
            return; // idempotent retry of the same confirmed plan
        }

        Collection? album = await _db.Collections
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null || album.Origin == CollectionOrigin.Proposed)
        {
            throw new InvalidOperationException(
                "The album is no longer available for moving originals.");
        }

        int[] assetIds = [.. files.Select(file => file.AssetId).Distinct()];
        if (assetIds.Length != files.Count)
        {
            throw new InvalidOperationException("The move plan contains the same asset twice.");
        }

        int members = await _db.CollectionMembers
            .CountAsync(
                member => member.CollectionId == collectionId
                          && assetIds.Contains(member.AssetId),
                cancellationToken)
            .ConfigureAwait(false);
        if (members != files.Count)
        {
            throw new InvalidOperationException(
                "The album changed after the move was checked. Review it and try again.");
        }

        bool active = await _db.AlbumFileMoves.AnyAsync(
            move => assetIds.Contains(move.AssetId)
                    && (move.State == AlbumFileMoveState.Planned
                        || move.State == AlbumFileMoveState.FileMoved),
            cancellationToken).ConfigureAwait(false);
        if (active)
        {
            throw new InvalidOperationException(
                "An interrupted move for this album still needs to be recovered. Reopen the "
                + "library, then try again.");
        }

        var expected = files.ToDictionary(file => file.AssetId);
        var current = await _db.Assets
            .AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id))
            .Select(asset => new
            {
                asset.Id,
                asset.PhotoSourceId,
                asset.RelativePath,
                asset.Length,
                asset.ModifiedUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current.Count != files.Count || current.Any(asset =>
            !Matches(asset.PhotoSourceId, asset.RelativePath, asset.Length, asset.ModifiedUtc,
                expected[asset.Id])))
        {
            throw new InvalidOperationException(
                "A photo changed after the move was checked. Scan the source, then try again.");
        }

        int sourceId = files[0].PhotoSourceId;
        string[] occupied = [.. await _db.Assets
            .AsNoTracking()
            .Where(asset => asset.PhotoSourceId == sourceId && !assetIds.Contains(asset.Id))
            .Select(asset => asset.RelativePath)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)];
        var occupiedPaths = new HashSet<string>(occupied, StringComparer.OrdinalIgnoreCase);
        if (files.Any(file => occupiedPaths.Contains(file.DestinationRelativePath)))
        {
            throw new InvalidOperationException(
                "Another indexed photo now uses one of the destination names. Check the folder "
                + "again and retry.");
        }

        DateTime now = DateTime.UtcNow;
        _db.AlbumFileMoves.AddRange(files.Select(file => new AlbumFileMove
        {
            OperationId = operationId,
            CollectionId = collectionId,
            AssetId = file.AssetId,
            PhotoSourceId = file.PhotoSourceId,
            SourceRelativePath = file.SourceRelativePath,
            DestinationRelativePath = file.DestinationRelativePath,
            ExpectedLength = file.ExpectedLength,
            ExpectedModifiedUtc = file.ExpectedModifiedUtc,
            State = AlbumFileMoveState.Planned,
            StartedUtc = now,
        }));

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.AlbumFileMoves
            .AsNoTracking()
            .Where(move => move.State == AlbumFileMoveState.Planned
                           || move.State == AlbumFileMoveState.FileMoved)
            .OrderBy(move => move.StartedUtc)
            .Select(move => move.OperationId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlbumMoveJournalEntry>> GetOperationAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        return await _db.AlbumFileMoves
            .AsNoTracking()
            .Where(move => move.OperationId == operationId)
            .OrderBy(move => move.Id)
            .Join(
                _db.PhotoSources.AsNoTracking(),
                move => move.PhotoSourceId,
                source => source.Id,
                (move, source) => new AlbumMoveJournalEntry(
                    move.Id,
                    move.OperationId,
                    move.CollectionId,
                    move.AssetId,
                    move.PhotoSourceId,
                    source.Path,
                    move.SourceRelativePath,
                    move.DestinationRelativePath,
                    move.ExpectedLength,
                    move.ExpectedModifiedUtc,
                    move.State,
                    move.Error))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkFileMovedAsync(
        int journalId, CancellationToken cancellationToken = default)
    {
        AlbumFileMove? move = await _db.AlbumFileMoves
            .FirstOrDefaultAsync(row => row.Id == journalId, cancellationToken)
            .ConfigureAwait(false);

        if (move is null || move.State is AlbumFileMoveState.Completed or AlbumFileMoveState.Failed)
        {
            return;
        }

        move.State = AlbumFileMoveState.FileMoved;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(int journalId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        AlbumFileMove? move = await _db.AlbumFileMoves
            .FirstOrDefaultAsync(row => row.Id == journalId, cancellationToken)
            .ConfigureAwait(false);

        if (move is null || move.State == AlbumFileMoveState.Completed)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (move.State == AlbumFileMoveState.Failed)
        {
            throw new InvalidOperationException("A failed move cannot be completed.");
        }

        var asset = await _db.Assets
            .FirstOrDefaultAsync(row => row.Id == move.AssetId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The asset row disappeared before its new path could be recorded.");

        if (asset.PhotoSourceId != move.PhotoSourceId)
        {
            throw new InvalidOperationException(
                "The asset now belongs to a different photo source.");
        }

        if (string.Equals(
            asset.RelativePath, move.SourceRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            asset.RelativePath = move.DestinationRelativePath;
        }
        else if (!string.Equals(
            asset.RelativePath, move.DestinationRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The asset path changed to a third location while its file was being moved.");
        }

        move.State = AlbumFileMoveState.Completed;
        move.Error = null;
        move.FinishedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FailAsync(
        int journalId, string error, CancellationToken cancellationToken = default)
    {
        AlbumFileMove? move = await _db.AlbumFileMoves
            .FirstOrDefaultAsync(row => row.Id == journalId, cancellationToken)
            .ConfigureAwait(false);

        if (move is null || move.State == AlbumFileMoveState.Completed)
        {
            return;
        }

        move.State = AlbumFileMoveState.Failed;
        move.Error = error.Length <= 1024 ? error : error[..1024];
        move.FinishedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool Matches(
        int sourceId,
        string relativePath,
        long length,
        DateTime modifiedUtc,
        AlbumMoveJournalPlan expected) =>
        sourceId == expected.PhotoSourceId
        && string.Equals(relativePath, expected.SourceRelativePath,
            StringComparison.OrdinalIgnoreCase)
        && length == expected.ExpectedLength
        && Math.Abs((modifiedUtc - expected.ExpectedModifiedUtc).TotalSeconds) < 2d;
}
