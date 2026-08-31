using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Application.Ports;

/// <summary>Album facts and the durable journal for moving album originals.</summary>
public interface IAlbumFileMoveRepository
{
    Task<AlbumMoveAlbum?> FindAlbumAsync(
        int collectionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumMoveAsset>> GetAlbumAssetsAsync(
        int collectionId, CancellationToken cancellationToken = default);

    Task BeginAsync(
        Guid operationId,
        int collectionId,
        IReadOnlyList<AlbumMoveJournalPlan> files,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetActiveOperationsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumMoveJournalEntry>> GetOperationAsync(
        Guid operationId, CancellationToken cancellationToken = default);

    Task MarkFileMovedAsync(int journalId, CancellationToken cancellationToken = default);

    /// <summary>Changes the asset path and completes its journal row atomically.</summary>
    Task CompleteAsync(int journalId, CancellationToken cancellationToken = default);

    Task FailAsync(
        int journalId, string error, CancellationToken cancellationToken = default);
}

public sealed record AlbumMoveAlbum(
    int Id,
    string Name,
    CollectionOrigin Origin,
    int PhotoCount);

public sealed record AlbumMoveAsset(
    int AssetId,
    int PhotoSourceId,
    string SourceRoot,
    string RelativePath,
    long Length,
    DateTime ModifiedUtc);

public sealed record AlbumMoveJournalPlan(
    int AssetId,
    int PhotoSourceId,
    string SourceRelativePath,
    string DestinationRelativePath,
    long ExpectedLength,
    DateTime ExpectedModifiedUtc);

public sealed record AlbumMoveJournalEntry(
    int Id,
    Guid OperationId,
    int CollectionId,
    int AssetId,
    int PhotoSourceId,
    string SourceRoot,
    string SourceRelativePath,
    string DestinationRelativePath,
    long ExpectedLength,
    DateTime ExpectedModifiedUtc,
    AlbumFileMoveState State,
    string? Error);
