namespace PhotoGallery.Application.Ports;

/// <summary>Walks a photo source and reports the media files under it.</summary>
public interface IMediaFileWalker
{
    /// <summary>
    /// Begins a walk of <paramref name="root"/>. The files stream lazily, but
    /// whether the root could be listed at all is settled before this returns.
    /// </summary>
    /// <remarks>
    /// Streaming rather than returning a list: on a large share the first results
    /// should reach the UI immediately, not after the whole tree is walked.
    ///
    /// <para>It returns a walk rather than a bare sequence so that an unreachable
    /// folder cannot read as an empty one. An empty sequence means the folder
    /// holds no media; a folder that could not be read says so.</para>
    /// </remarks>
    MediaWalk Walk(string root, CancellationToken cancellationToken = default);
}
