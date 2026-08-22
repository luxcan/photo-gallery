namespace PhotoGallery.Application.Ports;

/// <summary>
/// The cached preview images inside the working folder.
/// </summary>
/// <remarks>
/// Files, not database blobs: over a gigabyte of JPEG inside SQLite would make
/// the index unwieldy and every backup enormous, while the row only needs to
/// know the name.
/// </remarks>
public interface IThumbnailStore
{
    /// <summary>
    /// Stores both renditions and returns the name to record against the asset.
    /// </summary>
    /// <remarks>
    /// The name comes from the picture itself rather than from the row that
    /// happens to hold it, so re-running a pass overwrites what it wrote before
    /// instead of accumulating orphans beside it.
    /// </remarks>
    Task<string> SaveAsync(
        GeneratedThumbnail thumbnail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What a rendition of this content would be called, without producing it.
    /// </summary>
    /// <remarks>
    /// So that a pass can ask whether the work is already done before spending
    /// the read that would do it. A photograph's identity is only known once its
    /// bytes have been hashed, so this tells it nothing it did not already know
    /// - but a video's frames are named from facts a scan collected for free,
    /// and that makes the question answerable before the file is opened at all.
    /// </remarks>
    string NameFor(string contentHash);

    /// <summary>Small rendition, for the gallery grid.</summary>
    string ResolveTilePath(string thumbnailName);

    /// <summary>Large rendition, for viewing one photo and for face detection.</summary>
    string ResolvePreviewPath(string thumbnailName);

    /// <summary>
    /// Whether the tile is actually on disk. Takes a nullable name because the
    /// caller is usually asking about a row's claim, which may be null or may
    /// point at a file that no longer exists.
    /// </summary>
    bool Exists(string? thumbnailName);

    /// <summary>
    /// When the preview was last written, or null when there is not one.
    /// </summary>
    /// <remarks>
    /// So that a pass reading previews can tell whether what it recorded still
    /// describes the file it read. A rendition can be rewritten under the same
    /// name - it is named after the original's content, and the original has not
    /// changed - so the row's own marker cannot say that anything moved. The disk
    /// can.
    /// </remarks>
    DateTime? PreviewWrittenUtc(string? thumbnailName);

    /// <summary>
    /// Removes both renditions and reports whether neither is left on disk. A
    /// name that was already gone counts as removed; a file something else is
    /// holding does not.
    /// </summary>
    /// <remarks>
    /// Detaching deletes a record's files before its row, so it has to be told
    /// the truth. The previous version returned nothing and swallowed every
    /// failure, and the caller counted each one as reclaimed.
    /// </remarks>
    bool TryDelete(string? thumbnailName);

    /// <summary>
    /// Every rendition the store actually holds, whatever the index claims.
    /// </summary>
    /// <remarks>
    /// Renditions are named after the picture, so a photo whose bytes changed
    /// between two prepare passes leaves its previous pair of files behind with
    /// nothing naming them. Detaching the library's last source sweeps those up,
    /// which is the only way they can ever be found.
    /// </remarks>
    IReadOnlyCollection<string> ListStoredNames();

    /// <summary>Removes the shard directories that no longer hold anything.</summary>
    void RemoveEmptyShards();
}
