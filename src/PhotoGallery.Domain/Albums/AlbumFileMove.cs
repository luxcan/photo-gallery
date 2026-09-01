namespace PhotoGallery.Domain.Albums;

/// <summary>
/// The durable hand-off between moving one original and changing its indexed path.
/// </summary>
/// <remarks>
/// A file system and SQLite cannot share a transaction. This row is written
/// first so an interrupted move can tell whether to move the file, settle the
/// database, or leave both alone for a person to inspect.
///
/// <para>The ids are deliberately not foreign keys. A scan or a deletion must
/// not cascade away the evidence needed to reconcile a file already moved.</para>
/// </remarks>
public sealed class AlbumFileMove
{
    public int Id { get; set; }

    public Guid OperationId { get; set; }

    public int AlbumId { get; set; }

    public int AssetId { get; set; }

    public int PhotoSourceId { get; set; }

    public required string SourceRelativePath { get; set; }

    public required string DestinationRelativePath { get; set; }

    public long ExpectedLength { get; set; }

    public DateTime ExpectedModifiedUtc { get; set; }

    public AlbumFileMoveState State { get; set; }

    public string? Error { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? FinishedUtc { get; set; }
}
