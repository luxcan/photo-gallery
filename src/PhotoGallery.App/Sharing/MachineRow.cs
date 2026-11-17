namespace PhotoGallery.App.Sharing;

/// <summary>
/// One other computer, as the list shows it.
/// </summary>
/// <remarks>
/// The recency is worked out once, when the screen is read, rather than bound to
/// a clock. A line that silently rewrote itself from "up to date" to "1 hour
/// ago" while somebody looked at it would be a screen that appears to be doing
/// something.
/// </remarks>
/// <param name="Waiting">
/// True for a computer that has shared answers this library has not taken. What
/// the button is for, and worth marking rather than leaving the user to compare
/// dates.
/// </param>
public sealed record MachineRow(string Name, string Recency, bool Waiting)
{
    /// <summary>The computer, so that an answer about it can be recorded.</summary>
    public Guid MachineId { get; init; }

    /// <summary>
    /// True for a computer somebody here said "not this one" to, which the row
    /// says and offers to undo.
    /// </summary>
    public bool Ignored { get; init; }

    /// <summary>
    /// What it calls itself, or null for a computer not written down here,
    /// which cannot be given a name of its own yet.
    /// </summary>
    public string? OwnName { get; init; }

    /// <summary>The name somebody here gave it, or null where it has none.</summary>
    public string? FamiliarName { get; init; }

    /// <summary>Whether the row offers to rename it.</summary>
    public bool CanBeNamed => OwnName is not null;

    /// <summary>
    /// What the rename box starts with: the name it goes by, never the
    /// description a computer not synced with yet is shown under.
    /// </summary>
    public string? NameToEdit => FamiliarName ?? OwnName;

    /// <summary>What the pencil is called to somebody who cannot see it.</summary>
    public string RenameLabel => $"Rename {NameToEdit ?? Name}";
}
