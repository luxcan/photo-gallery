namespace PhotoGallery.Application.Ports;

/// <summary>
/// One walk of a photo source: the files it found, and what it could not read.
/// </summary>
/// <remarks>
/// A walk is not just a sequence of files, because "nothing came back" has two
/// opposite meanings - a folder someone emptied, and a folder that is not there
/// today - and only the walker can tell them apart. Reading an empty sequence as
/// proof that every file had gone once deleted an entire library's index.
///
/// <para><see cref="Files"/> is lazy, so <see cref="UnreadableFolders"/> fills as
/// it is drained and is complete only once it has run out.
/// <see cref="RootUnreadable"/> is the exception: it is settled before the first
/// file, so a caller can give up without enumerating anything.</para>
/// </remarks>
/// <param name="Root">
/// The root as the walker normalised it. Relative paths in the index were made
/// against this exact string, so a caller comparing the two must use it rather
/// than the path it passed in.
/// </param>
public sealed record MediaWalk(
    string Root,
    bool RootUnreadable,
    IReadOnlyCollection<string> UnreadableFolders,
    IEnumerable<ScannedFile> Files)
{
    /// <summary>A walk that never started, because its root could not be listed.</summary>
    public static MediaWalk Unreachable(string root) => new(root, true, [], []);
}
