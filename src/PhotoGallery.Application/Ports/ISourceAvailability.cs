namespace PhotoGallery.Application.Ports;

/// <summary>
/// Whether a photo source can be reached at this moment.
/// </summary>
/// <remarks>
/// Asked before anything is done to an original, and asked of the source's
/// <em>root</em> rather than of the file itself. The file cannot answer it: a
/// photograph somebody deleted and a photograph on a share that is not there
/// both come back as "not found", and acting on that alone is how a library
/// forgets pictures that are perfectly safe.
///
/// <para>The root can answer it. If the root lists, the share is up and a file
/// that is missing from it really is missing. If the root will not list, nothing
/// below it is known and nothing below it may be acted on.</para>
/// </remarks>
public interface ISourceAvailability
{
    /// <summary>
    /// Whether this root can be listed right now.
    /// </summary>
    /// <remarks>
    /// False means "cannot tell", never "empty". A caller must treat it as a
    /// reason to do nothing rather than as a fact about what is there.
    /// </remarks>
    bool CanReach(string sourceRoot);
}
