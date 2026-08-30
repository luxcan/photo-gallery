namespace PhotoGallery.Domain.Sharing;

/// <summary>Another machine in the house that this library has heard from.</summary>
public sealed class KnownMachine
{
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
}
