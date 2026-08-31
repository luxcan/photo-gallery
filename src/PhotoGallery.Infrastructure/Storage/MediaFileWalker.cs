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

            foreach (string child in listing.Directories)
            {
                // Never index the app's own thumbnails, models or quarantine,
                // even when the working folder doubles as a photo source. These
                // are choices rather than failures, so they are not recorded as
                // unreadable - doing so would protect stale rows under them from
                // ever being cleaned up.
                if (_workingFolder.IsAppOwned(child)
                    || IsSystemFolder(child)
                    || IsDirectoryLink(child))
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

            foreach (string file in listing.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!MediaFileTypes.IsMedia(file))
                {
                    continue;
                }

                ScannedFile? scanned = Describe(file, root);
                if (scanned is not null)
                {
                    yield return scanned.Value;
                }
                else
                {
                    // Its folder listed, but this file would not answer. Treating
                    // that as absence would delete a row for a file that is very
                    // likely still there, so the folder counts as incompletely
                    // read and nothing under it is judged missing.
                    unreadable.Add(listing.Path);
                }
            }
        }
    }

    private static ScannedFile? Describe(string file, string root)
    {
        try
        {
            var info = new FileInfo(file);
            return new ScannedFile(
                Path.GetRelativePath(root, file),
                info.Length,
                info.LastWriteTimeUtc,
                info.CreationTimeUtc);
        }
        catch (FileNotFoundException)
        {
            // Deleted between listing and stat. This one really is gone, and
            // saying so is the point of the separate catch.
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
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
    private static bool IsDirectoryLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
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
    /// Both listings together, so a directory cannot half-succeed and leave the
    /// caller believing it saw everything in it.
    /// </remarks>
    private static bool TryList(string directory, out DirectoryListing listing)
    {
        try
        {
            listing = new DirectoryListing(
                directory,
                [.. Directory.EnumerateDirectories(directory)],
                [.. Directory.EnumerateFiles(directory)]);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            listing = default;
            return false;
        }
    }

    private readonly record struct DirectoryListing(
        string Path, List<string> Directories, List<string> Files);
}
