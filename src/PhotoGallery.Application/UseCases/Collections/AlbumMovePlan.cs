namespace PhotoGallery.Application.UseCases.Collections;

/// <summary>An immutable, preflighted album move ready to show for confirmation.</summary>
public sealed record AlbumMovePlan(
    Guid OperationId,
    int CollectionId,
    string AlbumName,
    int PhotoSourceId,
    string SourceRoot,
    string DestinationFolder,
    IReadOnlyList<AlbumMovePlanItem> Items,
    int AlreadyThere,
    int Renamed,
    long TotalBytes)
{
    public int Moving => Items.Count;
}

public sealed record AlbumMovePlanItem(
    int AssetId,
    string SourceRelativePath,
    string DestinationRelativePath,
    string SourceFullPath,
    string DestinationFullPath,
    long ExpectedLength,
    DateTime ExpectedModifiedUtc,
    bool WasRenamed);

public sealed record AlbumMoveProgress(
    string FileName,
    int Done,
    int Total,
    long BytesDone,
    long TotalBytes)
{
    public double Fraction => TotalBytes > 0
        ? Math.Clamp((double)BytesDone / TotalBytes, 0d, 1d)
        : Total == 0 ? 1d : Math.Clamp((double)Done / Total, 0d, 1d);
}

public sealed record AlbumMoveResult(
    int Moved,
    int AlreadyThere,
    int Renamed,
    int Failed,
    bool WasCancelled,
    IReadOnlyList<string> Errors)
{
    public string Summary
    {
        get
        {
            string moved = Moved == 1 ? "Moved 1 original" : $"Moved {Moved:N0} originals";
            string result = AlreadyThere > 0
                ? $"{moved}; {AlreadyThere:N0} already in the folder."
                : $"{moved}.";

            if (Failed > 0)
            {
                result += $" {Failed:N0} could not be moved.";
            }

            if (WasCancelled)
            {
                result += " Stopped before the rest were changed.";
            }

            return result;
        }
    }

    public static AlbumMoveResult Nothing(int alreadyThere = 0) =>
        new(0, alreadyThere, 0, 0, false, []);
}
