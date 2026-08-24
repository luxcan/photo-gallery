namespace PhotoGallery.Application.UseCases.Scanning;

/// <summary>What a scan of one photo source changed.</summary>
/// <param name="WasUnavailable">
/// True when the folder itself could not be read, so the scan proved nothing.
/// Distinct from finding it empty: an emptied folder is a real answer and its
/// rows go.
/// </param>
/// <param name="FoldersNotRead">
/// Folders below the root that refused to be listed. Nothing under them was
/// seen, so nothing under them could be judged missing.
/// </param>
/// <param name="Kept">
/// Indexed files left alone because the walk could not prove them gone - all of
/// them when the source was unavailable, those under an unreadable folder
/// otherwise.
/// </param>
public sealed record ScanResult(
    int PhotoSourceId,
    string SourcePath,
    int Added,
    int Updated,
    int Removed,
    int Unchanged,
    TimeSpan Elapsed,
    bool WasCancelled,
    bool WasUnavailable = false,
    int FoldersNotRead = 0,
    int Kept = 0)
{
    public int Seen => Added + Updated + Unchanged;

    /// <summary>Indexed files this source holds now, whether or not the walk finished.</summary>
    public int Indexed => Seen + Kept;

    public bool ChangedAnything => Added > 0 || Updated > 0 || Removed > 0;

    /// <summary>A scan that got nowhere, so the row it describes must not be touched.</summary>
    public static ScanResult Unavailable(
        int photoSourceId, string sourcePath, int indexed, TimeSpan elapsed) =>
        new(photoSourceId, sourcePath, 0, 0, 0, 0, elapsed,
            WasCancelled: false, WasUnavailable: true, FoldersNotRead: 0, Kept: indexed);

    public string Summary => WasUnavailable
        ? $"folder not reachable - nothing was changed, and its {Kept:N0} indexed "
        + "files were kept. Reconnect it and scan again"
        : WasCancelled
            ? $"cancelled after {Seen:N0} files"
            : $"{Seen:N0} files: {Added:N0} new, {Updated:N0} changed, "
            + $"{Removed:N0} gone, {Unchanged:N0} unchanged ({Elapsed.TotalSeconds:N1}s)"
            + (FoldersNotRead > 0
                ? $" - could not read {FoldersNotRead:N0} of its folders, so "
                + $"{Kept:N0} indexed files under them were kept"
                : string.Empty);
}
