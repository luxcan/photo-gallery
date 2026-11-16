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

    /// <summary>Whether two computers go by the same name, as a person reads it.</summary>
    /// <remarks>
    /// Asked because a planted file would choose exactly the name of a laptop
    /// in the house, and two rows reading "Mum's laptop" are what somebody
    /// would answer yes to twice.
    /// </remarks>
    public static bool SameName(string? one, string? other) =>
        string.Equals(NameFrom(one), NameFrom(other), StringComparison.CurrentCultureIgnoreCase);
}
