namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// What the sync folder said when it was asked whether it is there - or that it
/// said nothing in time.
/// </summary>
/// <remarks>
/// Worded twice, because it is read in two places that speak differently. The
/// Sync screen shows a sentence beside the folder it is about; a scan's log
/// finishes "Skipped, because" with a clause. Both are written here, beside
/// each other, so the two can never disagree about which of the three things
/// happened.
/// </remarks>
/// <param name="Folder">The folder that was asked, or empty where none is chosen.</param>
/// <param name="Problem">
/// Why the folder cannot be used this time, in the user's words and ready to
/// show, or empty when it can. Empty too when no folder is chosen: that is the
/// ordinary first state rather than a problem, and saying "choose a folder"
/// beside the button that chooses one tells somebody what they can already see.
/// </param>
/// <param name="Reason">
/// Why a scan's phase goes on without the folder, as the clause after
/// "Skipped, because", or null when the folder can be used.
/// </param>
public sealed record SyncFolderAnswer(string Folder, string Problem, string? Reason)
{
    /// <summary>Nothing to ask, because nobody has chosen a folder.</summary>
    public static SyncFolderAnswer NotChosen { get; } =
        new(string.Empty, string.Empty, "no sync folder is chosen");

    /// <summary>Whether the folder answered, and is there.</summary>
    public bool CanExchange => Reason is null;

    /// <summary>
    /// Whether a folder is chosen and cannot be used this time - which the Sync
    /// screen and its button both have to say, and stop at.
    /// </summary>
    public bool IsOutOfReach => Problem.Length > 0;

    /// <summary>The folder answered, and is there.</summary>
    public static SyncFolderAnswer There(string folder) => new(folder, string.Empty, null);

    /// <summary>
    /// The folder answered, and is not there: a drive not plugged in, a path
    /// mistyped.
    /// </summary>
    /// <param name="problem">
    /// What the folder's own question said, which is already the screen's
    /// sentence for it.
    /// </param>
    public static SyncFolderAnswer Unreachable(string folder, string problem) =>
        new(folder, problem, $"the sync folder cannot be reached: {folder}");

    /// <summary>
    /// The folder did not answer before the deadline: a drive asleep, or an
    /// address on a network that has gone, which the operating system takes
    /// longer than that to give up on.
    /// </summary>
    public static SyncFolderAnswer Silent(string folder, TimeSpan patience) =>
        new(
            folder,
            $"That folder did not answer within {patience.TotalSeconds:N0} seconds: {folder}",
            $"the sync folder did not answer within {patience.TotalSeconds:N0} seconds: {folder}");
}
