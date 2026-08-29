using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Storage;

/// <summary>
/// Stores thumbnails as files under the working folder.
/// </summary>
/// <remarks>
/// Names are sharded two characters deep - <c>f1\0000a3f1.jpg</c> - because a
/// single directory holding tens of thousands of files is slow to enumerate on
/// Windows and unpleasant to inspect by hand. The two renditions of one asset
/// sit side by side, the larger suffixed <c>-p</c>.
///
/// <para>A name is the first 32 characters of the original's content hash, not
/// its row id. Content is the only identity that survives a source being
/// detached and re-added, a share changing address, and the database renumbering
/// its rows - all of which would otherwise orphan every file here and force
/// 25 GB to be read again. It also spreads evenly over the 256 shards, which a
/// sequential id does not: ids below 16,777,216 all begin <c>00</c>, so sharding
/// on them put an entire library in one directory.</para>
///
/// <para>Two byte-identical photos therefore share one pair of files, which is
/// correct - they are the same picture.</para>
/// </remarks>
public sealed class FileSystemThumbnailStore : IThumbnailStore
{
    private const string PreviewSuffix = "-p";

    private const string Extension = ".jpg";

    private const int ShardLength = 2;

    private readonly IWorkingFolder _workingFolder;

    public FileSystemThumbnailStore(IWorkingFolder workingFolder) =>
        _workingFolder = workingFolder;

    public async Task<string> SaveAsync(
        GeneratedThumbnail thumbnail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);

        string name = NameFor(thumbnail.ContentHash);
        string tilePath = ResolveTilePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);

        await File.WriteAllBytesAsync(tilePath, thumbnail.Tile, cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(ResolvePreviewPath(name), thumbnail.Preview, cancellationToken)
            .ConfigureAwait(false);

        return name;
    }

    public string ResolveTilePath(string thumbnailName) =>
        Path.Combine(_workingFolder.ThumbnailsPath, Shard(thumbnailName), thumbnailName);

    public string ResolvePreviewPath(string thumbnailName)
    {
        string preview = Path.GetFileNameWithoutExtension(thumbnailName)
                       + PreviewSuffix
                       + Path.GetExtension(thumbnailName);
        return Path.Combine(_workingFolder.ThumbnailsPath, Shard(thumbnailName), preview);
    }

    public bool Exists(string? thumbnailName) =>
        !string.IsNullOrWhiteSpace(thumbnailName) && File.Exists(ResolveTilePath(thumbnailName));

    public DateTime? PreviewWrittenUtc(string? thumbnailName)
    {
        if (string.IsNullOrWhiteSpace(thumbnailName))
        {
            return null;
        }

        var file = new FileInfo(ResolvePreviewPath(thumbnailName));
        return file.Exists ? file.LastWriteTimeUtc : null;
    }

    public bool TryDelete(string? thumbnailName)
    {
        if (string.IsNullOrWhiteSpace(thumbnailName))
        {
            return true;
        }

        // A single & rather than &&: the preview must still be attempted when the
        // tile refuses, or a locked tile would strand its preview forever.
        return Gone(ResolveTilePath(thumbnailName)) & Gone(ResolvePreviewPath(thumbnailName));
    }

    public IReadOnlyCollection<string> ListStoredNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_workingFolder.ThumbnailsPath))
        {
            return names;
        }

        // Under thumbs\ only, never the working folder root: the root may itself
        // hold the user's pictures, which is why IsAppOwned refuses it.
        foreach (string file in Directory.EnumerateFiles(
                     _workingFolder.ThumbnailsPath, '*' + Extension, SearchOption.AllDirectories))
        {
            // Windows pattern matching also returns longer extensions, so the
            // search pattern narrows the walk but does not decide the answer.
            if (!Path.GetExtension(file).Equals(Extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(file);
            if (stem.EndsWith(PreviewSuffix, StringComparison.Ordinal))
            {
                // A preview belongs to its tile rather than being a name of its own.
                stem = stem[..^PreviewSuffix.Length];
            }

            names.Add(stem + Extension);
        }

        return names;
    }

    public void RemoveEmptyShards()
    {
        if (!Directory.Exists(_workingFolder.ThumbnailsPath))
        {
            return;
        }

        foreach (string shard in Directory.GetDirectories(_workingFolder.ThumbnailsPath))
        {
            try
            {
                // Never recursive: a shard that still holds a file throws and is
                // left alone, so this cannot delete something it was not asked to.
                Directory.Delete(shard);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A directory that will not go is untidiness, not a failed detach.
            }
        }
    }

    private static bool Gone(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (UnauthorizedAccessException)
        {
            // A working folder restored from a backup can arrive read-only, and a
            // detach that could never finish is worse than clearing one flag.
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still held. Whether that matters is the caller's decision.
            }
        }
        catch (IOException)
        {
            // Something else has the file open.
        }

        // Asked of the disk rather than assumed from the call above, because that
        // call may have failed silently.
        return !File.Exists(path);
    }

    private static string Shard(string thumbnailName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailName);

        string stem = Path.GetFileNameWithoutExtension(thumbnailName);
        return stem.Length < ShardLength ? stem.PadRight(ShardLength, '0') : stem[..ShardLength];
    }

    /// <summary>
    /// Derived from the original's content, so re-running a pass overwrites the
    /// previous file instead of leaving an orphan behind.
    /// </summary>
    /// <remarks>
    /// 32 hex characters is 128 bits, which is far more than 16,225 pictures
    /// need and keeps the path short - the repository already hit MAX_PATH once.
    /// </remarks>
    public string NameFor(string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        return RenditionName.For(contentHash);
    }

}
