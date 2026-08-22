namespace PhotoGallery.Application.UseCases.Gallery;

/// <summary>What a deletion actually took.</summary>
/// <param name="Refused">
/// The photographs that are still there: a file something else is holding, one
/// that is read-only, or a row that had already been removed. Their rows and
/// cached copies are untouched, so the library still matches the disk and
/// running this again finishes the job once whatever holds them lets go.
/// </param>
/// <param name="WasCancelled">
/// Whether it was stopped part way. What had already gone stays gone - each
/// photograph is deleted whole or not at all - so this means the rest were never
/// reached, not that anything was left half done.
/// </param>
/// <param name="OutOfReach">
/// The photographs nothing could be found out about, because their source could
/// not be reached. Kept apart from <paramref name="Refused"/> because the two
/// mean opposite things: a refusal is a fact about a file that is there, and
/// this is the absence of any fact at all. Nothing was touched for these - not
/// the file, not the cached copies, not the row.
/// </param>
/// <param name="UnreachableSources">
/// The roots behind <paramref name="OutOfReach"/>, named once each so a message
/// can say which share is away rather than listing every file on it.
/// </param>
public sealed record PhotoRemovalResult(
    int Deleted,
    IReadOnlyList<int> Refused,
    bool WasCancelled,
    IReadOnlyList<int> OutOfReach,
    IReadOnlyList<string> UnreachableSources)
{
    /// <summary>Nothing was asked for, so nothing happened.</summary>
    public static PhotoRemovalResult Nothing { get; } = new(0, [], false, [], []);

    /// <summary>
    /// Everything was left alone because its source could not be reached.
    /// </summary>
    public static PhotoRemovalResult AllOutOfReach(
        IReadOnlyList<int> assetIds, IReadOnlyList<string> sources) =>
        new(0, [], false, assetIds, sources);

    public string Summary => WasCancelled
        ? $"stopped - {Deleted:N0} deleted, the rest left alone"
        : OutOfReach.Count > 0
            ? $"{Deleted:N0} deleted, {OutOfReach.Count:N0} left alone - "
              + $"could not reach {string.Join(", ", UnreachableSources)}"
            : Refused.Count == 0
                ? $"{Deleted:N0} deleted"
                : $"{Deleted:N0} deleted, {Refused.Count:N0} would not go and are still there";
}
