namespace PhotoGallery.Application.Ports;

/// <summary>
/// How an attempt to read a photograph's coordinates ended.
/// </summary>
/// <remarks>
/// Three answers rather than a nullable pair, because the two ways of having no
/// coordinates must not be written down the same way. A camera without a
/// receiver is a settled answer and should never be asked again; a file that
/// could not be opened is not an answer at all, and recording it as one would
/// leave the photograph permanently unplaced the moment the share came back.
/// </remarks>
public enum CoordinateOutcome
{
    /// <summary>The file carries coordinates.</summary>
    Found,

    /// <summary>The file was read and carries none. Settled; do not ask again.</summary>
    None,

    /// <summary>The file could not be read. Nothing is known, so nothing is recorded.</summary>
    Unreadable,
}
