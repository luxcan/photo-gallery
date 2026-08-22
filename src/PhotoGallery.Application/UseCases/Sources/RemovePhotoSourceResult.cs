namespace PhotoGallery.Application.UseCases.Sources;

/// <summary>What detaching a photo source removed from the library.</summary>
/// <param name="AssetsRemoved">
/// Records whose cached copies were proved gone and whose rows then went with
/// them. Short of <paramref name="AssetsTotal"/> when the detach was stopped or
/// something refused to delete.
/// </param>
/// <param name="CachedCopiesReclaimed">
/// Renditions deleted from the working folder. Lower than the record count when
/// pictures were duplicated, since identical copies share one rendition, and
/// higher when a completed detach also swept up copies nothing referenced.
/// </param>
public sealed record RemovePhotoSourceResult(
    int AssetsRemoved,
    int AssetsTotal,
    int CachedCopiesReclaimed,
    int CouldNotDelete,
    TimeSpan Elapsed,
    bool WasCancelled)
{
    /// <summary>
    /// Whether the folder is actually gone. It only goes once every cached copy
    /// it owned has been proved off the disk.
    /// </summary>
    public bool WasDetached =>
        !WasCancelled && CouldNotDelete == 0 && AssetsRemoved == AssetsTotal;

    public string Summary => WasCancelled
        ? $"stopped - {AssetsRemoved:N0} of {AssetsTotal:N0} records removed; "
        + "the folder is still attached"
        : WasDetached
            ? $"{AssetsRemoved:N0} indexed files removed, "
            + $"{CachedCopiesReclaimed:N0} cached copies reclaimed "
            + $"({Elapsed.TotalSeconds:N1}s) - your own files are untouched"
            : $"still attached: {CouldNotDelete:N0} cached copies could not be deleted, "
            + "something else is using them. Close anything showing these pictures "
            + "and detach again.";
}
