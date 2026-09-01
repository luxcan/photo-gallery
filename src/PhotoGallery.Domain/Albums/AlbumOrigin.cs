namespace PhotoGallery.Domain.Albums;

/// <summary>Where an album came from, which decides what a pass may do to it.</summary>
public enum AlbumOrigin
{
    /// <summary>
    /// The app grouped it and is offering it. A rebuild may change or remove it.
    /// </summary>
    Proposed = 0,

    /// <summary>
    /// A proposal the user kept. A rebuild leaves its name and its members
    /// alone, but photographs of the same days found later still join it - a
    /// trip you kept should not quietly miss the photographs copied over
    /// afterwards.
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// The user made it themselves. No pass ever touches it: not its name, not
    /// its members, not its existence.
    /// </summary>
    Made = 2,
}
