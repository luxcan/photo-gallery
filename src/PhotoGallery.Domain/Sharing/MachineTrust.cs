namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Whether somebody on this computer has said to take another computer's
/// changes.
/// </summary>
/// <remarks>
/// <strong>Nothing a file says about itself can stand in for this.</strong>
/// Every check the merge makes - the machine's id, its version, its clock, a
/// folder in common - is a value the file declares, and anybody who can write
/// to the sync folder can read an honest machine's file and copy every one of
/// them. A person here saying "yes, that one is ours" is the only fact a
/// planted file cannot supply, and what that file could do without it - delete
/// every person and album, on every laptop - cannot be taken back.
/// </remarks>
public enum MachineTrust
{
    /// <summary>
    /// Heard from, and not answered yet. Its changes wait, untouched, until
    /// somebody says.
    /// </summary>
    Undecided = 0,

    /// <summary>Somebody here said to take its changes.</summary>
    Trusted = 1,

    /// <summary>
    /// Somebody here said "not this one". Its files are left unread until
    /// somebody changes their mind.
    /// </summary>
    Ignored = 2,
}
