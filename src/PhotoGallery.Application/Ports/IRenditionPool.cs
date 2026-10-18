using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// Where the cached pictures pool, so that a new machine does not have to make
/// its own.
/// </summary>
/// <remarks>
/// Separate from <see cref="IDecisionExchange"/>, and deliberately. Decisions
/// are one small document written whole; renditions are tens of thousands of
/// files copied one at a time and stopped halfway more often than not. Keeping
/// the two apart is also what lets a machine take the answers and decline the
/// gigabytes.
///
/// <para>No mapping is needed and none exists. A rendition is named after a hash
/// of the original's bytes, so two machines can pour their thumbnails into one
/// folder and cannot collide: the same name means the same bytes means the same
/// picture. Copying is "take the names I do not have", which is idempotent,
/// resumable and stoppable like every other pass - and a photograph whose bytes
/// change gets a new name, so nothing here is ever overwritten and "latest"
/// needs no version at all.</para>
/// </remarks>
public interface IRenditionPool
{
    /// <summary>Whether there is a pool to use, and why not when there is not.</summary>
    /// <remarks>
    /// <strong>Must not block its caller.</strong> The pool is a folder on
    /// another machine, and asking after one that is not answering holds the
    /// thread for as long as the operating system takes to give up. A scan races
    /// this against a deadline and goes on without the pool, which it can only
    /// do if the answer is still to come when the call returns.
    /// </remarks>
    Task<ExchangeReadiness> ReadinessAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes this machine's manifest, replacing whatever it wrote last.</summary>
    Task PublishAsync(PreparedSet mine, CancellationToken cancellationToken = default);

    /// <summary>Reads what every other machine has prepared.</summary>
    Task<IReadOnlyList<PreparedSet>> FetchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the faces this machine has found, with the models it found them
    /// with - unless the very same faces are already there - and answers
    /// whether it wrote them.
    /// </summary>
    /// <remarks>
    /// Its own file, not part of the manifest. The facts are a megabyte and are
    /// read on every exchange; the vectors are 40.5 MB and are worth reading
    /// once. A machine that wants the pictures and not the two hours of face
    /// detection simply does not ask for them.
    ///
    /// <para>Every scan gives, and most scans find no new face. Writing the
    /// whole file again each time would be tens of megabytes across the network
    /// to say nothing new, so a file whose faces have not changed since this
    /// machine wrote it is left alone.</para>
    /// </remarks>
    Task<bool> PublishFacesAsync(FaceSet mine, CancellationToken cancellationToken = default);

    /// <summary>Reads the faces every other machine has found.</summary>
    Task<IReadOnlyList<FaceSet>> FetchFacesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every rendition name the pool holds.
    /// </summary>
    /// <remarks>
    /// A listing, so that both directions can be worked out before a byte moves:
    /// what to offer is what the pool lacks, and what to fetch is what this
    /// machine lacks.
    /// </remarks>
    Task<IReadOnlyCollection<string>> NamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies one rendition's two files into the pool, and answers whether both
    /// arrived.
    /// </summary>
    /// <remarks>
    /// Through a temporary name and renamed into place, because two machines
    /// will push the same missing rendition at the same moment and a third must
    /// never read half a JPEG.
    /// </remarks>
    Task<bool> PushAsync(
        string thumbnailName,
        string tilePath,
        string previewPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies one rendition's two files out of the pool, and answers whether
    /// both arrived.
    /// </summary>
    /// <remarks>
    /// <strong>The preview is written before the tile.</strong>
    /// <see cref="IThumbnailStore.Exists"/> asks only about the tile, so a copy
    /// interrupted between the two would otherwise leave a photograph reporting
    /// itself complete with no preview - which is the file the viewer opens and
    /// the face detector reads. The same "poster last" discipline the video pass
    /// already keeps, for the same reason.
    /// </remarks>
    Task<bool> PullAsync(
        string thumbnailName,
        string tilePath,
        string previewPath,
        CancellationToken cancellationToken = default);
}
