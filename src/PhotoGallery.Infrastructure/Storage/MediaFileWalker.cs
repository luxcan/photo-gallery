using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Storage;

/// <summary>
/// Walks a folder tree for media files.
/// </summary>
/// <remarks>
/// The traversal is explicit rather than <c>SearchOption.AllDirectories</c>,
/// because that overload aborts the whole enumeration on the first unreadable
/// folder. On a real library - a network share, a drive with a System Volume
/// Information folder - that is a certainty, not an edge case. Walking a stack
/// lets one unreadable folder be skipped while the rest of the scan continues.
///
/// <para>Skipping it is not the same as forgetting it. Every folder that refused
/// to be listed is named in the walk, because the caller uses "this file was not
/// seen" to decide that a file is gone, and a folder nobody could open never
/// showed what was in it.</para>
///
/// <para>Each folder is listed once, and a file's size and dates are taken from
/// that listing rather than asked for again. On a network share every separate
/// question is a round trip; asking one per file made a re-scan of an unchanged
/// library take minutes instead of seconds, for exactly the same answers.</para>
/// </remarks>
public sealed class MediaFileWalker : IMediaFileWalker
{
    private readonly IWorkingFolder _workingFolder;

    public MediaFileWalker(IWorkingFolder workingFolder) => _workingFolder = workingFolder;

    public MediaWalk Walk(string root, CancellationToken cancellationToken = default)
    {
        // Eager, unlike the enumeration below: a caller that never drains the
        // files still deserves to be told its argument was blank.
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string normalisedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        // The root is listed here rather than inside the loop. An offline share
        // and an empty folder both produce no files, and this is the only call
        // that can tell them apart, so it has to happen before anything is
        // yielded and its answer has to survive into the walk.
        if (!TryList(normalisedRoot, out DirectoryListing rootListing))
        {
            return MediaWalk.Unreachable(normalisedRoot);
        }

        var unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new MediaWalk(
            normalisedRoot,
            RootUnreadable: false,
            unreadable,
            Enumerate(normalisedRoot, rootListing, unreadable, cancellationToken));
    }

    private IEnumerable<ScannedFile> Enumerate(
        string root,
        DirectoryListing rootListing,
        HashSet<string> unreadable,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<DirectoryListing>();
        pending.Push(rootListing);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryListing listing = pending.Pop();

            foreach (DirectoryInfo directory in listing.Directories)
            {
                string child = directory.FullName;

                // Never index the app's own thumbnails, models or quarantine,
                // even when the working folder doubles as a photo source. These
                // are choices rather than failures, so they are not recorded as
                // unreadable - doing so would protect stale rows under them from
                // ever being cleaned up.
                if (_workingFolder.IsAppOwned(child)
                    || IsSystemFolder(child)
                    || IsDirectoryLink(directory))
                {
                    continue;
                }

                if (TryList(child, out DirectoryListing childListing))
                {
                    pending.Push(childListing);
                }
                else
                {
                    // Everything below here is now unknown, not absent.
                    unreadable.Add(child);
                }
            }

            foreach (FileInfo file in listing.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!MediaFileTypes.IsMedia(file.Name))
                {
                    continue;
                }

                // Read from what the listing already said, not by asking the
                // file again: these properties were filled in by the listing.
                yield return new ScannedFile(
                    Path.GetRelativePath(root, file.FullName),
                    file.Length,
                    file.LastWriteTimeUtc,
                    file.CreationTimeUtc);
            }
        }
    }

    private static bool IsSystemFolder(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)
            || name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("@eaDir", StringComparison.OrdinalIgnoreCase); // NAS thumbnail cache
    }

    /// <summary>
    /// Directory links and junctions are aliases, not children to traverse.
    /// Skipping them prevents a link back to an ancestor from making the walk
    /// cycle forever and avoids indexing the same tree through several names.
    /// </summary>
    /// <remarks>
    /// Only a folder the listing marked as a reparse point is asked for its
    /// target, because every link carries that mark. The mark alone is not
    /// enough to skip on: cloud placeholders and deduplicated folders carry it
    /// too, and they hold real files.
    /// </remarks>
    private static bool IsDirectoryLink(DirectoryInfo directory) =>
        IsLink(directory.Attributes, () => directory.LinkTarget);

    /// <summary>
    /// True for a folder that is an alias of another, given its listed
    /// attributes and a way to ask where it points.
    /// </summary>
    internal static bool IsLink(FileAttributes attributes, Func<string?> linkTarget)
    {
        if (!attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        try
        {
            return linkTarget() is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // TryList below records it as unreadable if it still cannot be read.
            return false;
        }
    }

    /// <summary>
    /// Lists a directory's children in one go, or reports that it would not open.
    /// </summary>
    /// <remarks>
    /// One listing for folders and files together, read to the end inside the
    /// try: entries arrive as the listing is read, so an error part-way through
    /// surfaces here and the directory cannot half-succeed and leave the caller
    /// believing it saw everything in it.
    ///
    /// <para>The overload without options, deliberately. It skips nothing, the
    /// same as the listing this replaced. A default <see cref="EnumerationOptions"/>
    /// would leave out hidden and system files and turn a folder that refuses to
    /// open into an empty one - and both would read as files that had gone.</para>
    /// </remarks>
    private static bool TryList(string directory, out DirectoryListing listing)
    {
        try
        {
            var directories = new List<DirectoryInfo>();
            var files = new List<FileInfo>();
            foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child)
                {
                    directories.Add(child);
                }
                else if (entry is FileInfo file)
                {
                    files.Add(file);
                }
            }

            listing = new DirectoryListing(directory, directories, files);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            listing = default;
            return false;
        }
    }

    private readonly record struct DirectoryListing(
        string Path, List<DirectoryInfo> Directories, List<FileInfo> Files);
}
