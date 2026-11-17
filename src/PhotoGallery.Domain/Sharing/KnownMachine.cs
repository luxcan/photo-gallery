namespace PhotoGallery.Domain.Sharing;

/// <summary>Another machine in the house that this library has heard from.</summary>
public sealed class KnownMachine
{
    /// <summary>The longest name kept for a computer.</summary>
    public const int NameLength = 128;

    /// <summary>Said for a computer whose file gives it no name.</summary>
    public const string NoName = "A computer with no name";

    public int Id { get; set; }

    /// <summary>The <see cref="Guid"/> that machine minted for itself, once.</summary>
    public Guid MachineId { get; set; }

    /// <summary>What it calls itself. Editable there, and carries no meaning here.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// What somebody here chose to call it, or null to go by <see cref="Name"/>.
    /// </summary>
    /// <remarks>
    /// Kept on this computer only. No file in the sync folder can set it and
    /// none carries it: a laptop that calls itself LAPTOP-AB12CD is "Kitchen
    /// laptop" here because somebody here said so, and a planted file cannot
    /// take that name by calling itself it.
    /// </remarks>
    public string? FamiliarName { get; set; }

    /// <summary>What the screen calls it: the familiar name if it has one.</summary>
    public string CalledHere => FamiliarName ?? Name;

    /// <summary>
    /// When this library last took that machine's answers.
    /// </summary>
    /// <remarks>
    /// <strong>Shown, never consulted.</strong> Nothing is fetched or skipped on
    /// it: the merge reads the whole state every time, which is the point of
    /// being state-based rather than a log. It exists because the shared folder's
    /// whole advantage is that it does not need two laptops on at once - which
    /// also means presence is not a thing it can report. Recency is, and without
    /// it a decision set written six months ago by a laptop now in a drawer
    /// merges exactly like one written an hour ago, with nothing on screen to
    /// tell them apart.
    /// </remarks>
    public DateTime? LastMergedUtc { get; set; }

    /// <summary>
    /// Whether somebody here has said to take its changes. Nothing it has
    /// written is used until they have.
    /// </summary>
    /// <remarks>
    /// Every machine a library already knew when this was added counts as
    /// said: it had been taken from, by somebody pressing Sync, and asking the
    /// whole house again about laptops it has synced with for months would be
    /// a question nobody reads before answering.
    /// </remarks>
    public MachineTrust Trust { get; set; }

    /// <summary>
    /// What a computer is called here, from what its own file says it is
    /// called.
    /// </summary>
    /// <remarks>
    /// A file in the sync folder can say anything, including nothing. A missing
    /// name once failed the row it is kept in, and with it every Sync - and a
    /// computer that cannot be written down cannot be refused either. A blank
    /// one put "Take changes from ?" on screen, and answering it closed the
    /// app.
    /// </remarks>
    public static string NameFrom(string? said)
    {
        string name = said?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return NoName;
        }

        return name.Length <= NameLength ? name : name[..NameLength].TrimEnd();
    }

    /// <summary>
    /// The familiar name somebody typed, as it is kept: trimmed and cut to the
    /// column, or null where it is blank or is only the computer's own name.
    /// </summary>
    /// <remarks>
    /// Blank means "go back to what it calls itself", which is what clearing
    /// the box says - and a familiar name equal to the computer's own would be
    /// a copy that goes stale the day the computer is renamed there.
    /// </remarks>
    public static string? FamiliarNameFrom(string? typed, string ownName)
    {
        string name = typed?.Trim() ?? string.Empty;

        if (name.Length > NameLength)
        {
            name = name[..NameLength].TrimEnd();
        }

        return name.Length == 0 || string.Equals(name, ownName, StringComparison.Ordinal)
            ? null
            : name;
    }

    /// <summary>
    /// Every name this computer goes by here: what it calls itself, and the
    /// name given here where it has one.
    /// </summary>
    public IEnumerable<string> NamesGoneBy() =>
        FamiliarName is null ? [Name] : [Name, FamiliarName];

    /// <summary>
    /// The name this computer and another could be taken for each other by,
    /// or null where they share none.
    /// </summary>
    /// <remarks>
    /// Any name either goes by against any name the other does. A file calling
    /// itself what this screen calls the kitchen laptop reads as that laptop
    /// just as a copy of its own name does, and a name given here that another
    /// computer calls itself puts two rows of that name on screen. One rule,
    /// so that the Sync screen, the same-folder question and the rename box
    /// never disagree about whether two computers look alike.
    /// </remarks>
    public string? NameSharedWith(KnownMachine other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return NamesGoneBy().FirstOrDefault(mine =>
            other.NamesGoneBy().Any(theirs => SameName(mine, theirs)));
    }

    /// <summary>Whether two computers go by the same name, as a person reads it.</summary>
    /// <remarks>
    /// Asked because a planted file would choose exactly the name of a laptop
    /// in the house, and two rows reading "Mum's laptop" are what somebody
    /// would answer yes to twice.
    /// </remarks>
    public static bool SameName(string? one, string? other) =>
        string.Equals(NameFrom(one), NameFrom(other), StringComparison.CurrentCultureIgnoreCase);
}
