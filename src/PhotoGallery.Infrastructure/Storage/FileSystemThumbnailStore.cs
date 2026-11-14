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

    private const string TempExtension = ".tmp";

    private const int ShardLength = 2;

    /// <summary>
    /// How one shard is read when every rendition is listed.
    /// </summary>
    /// <remarks>
    /// Hidden and system files are not skipped, unlike a default
    /// <see cref="EnumerationOptions"/>: a working folder restored from a backup
    /// can arrive with its attributes changed, and the files are just as there.
    /// A folder that refuses to open is not ignored here: ignoring it also covers
    /// the shard itself, which then lists as empty without a word, so the refusal
    /// is left to reach <see cref="Listed"/> and be told or thrown.
    /// </remarks>
    private static readonly EnumerationOptions s_shardListing = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

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

        // The tile is the readiness marker, so it lands last. Exists also checks
        // both files, but this order keeps an interrupted write incomplete in
        // the same direction as copies arriving from the shared pool.
        await PutAsync(ResolvePreviewPath(name), thumbnail.Preview, cancellationToken)
            .ConfigureAwait(false);
        await PutAsync(tilePath, thumbnail.Tile, cancellationToken)
            .ConfigureAwait(false);

        return name;
    }

    /// <summary>
    /// Puts one rendition in place, however many others are putting the same one
    /// there at the same moment.
    /// </summary>
    /// <remarks>
    /// Byte-identical photographs share a name, and a scan prepares several
    /// photographs at once - so a folder of copies has two of them writing one
    /// file together. Written straight to the name, the second open failed on
    /// the first one's handle and the IOException ended the whole scan: a
    /// library given 300 copies of four photographs could not finish one, where
    /// 300 different photographs finished in the ordinary way.
    ///
    /// <para>Each write now goes to a temporary of its own and is renamed into
    /// place, so no two writers ever hold one file and a reader never finds half
    /// of one. A rename refused while the name is there - another writer landed
    /// first and something has it open, or the gallery is drawing it - is
    /// success rather than failure: the name is the content, so what is there is
    /// what this was writing. Anything else is still thrown, and the pass decides
    /// what it costs.</para>
    ///
    /// <para>A temporary is left behind only if the app itself dies between the
    /// write and the rename. It ends in <c>.tmp</c>, and nothing that lists the
    /// pictures here counts one.</para>
    /// </remarks>
    private static async Task PutAsync(
        string path, byte[] rendition, CancellationToken cancellationToken)
    {
        try
        {
            await WholeFile
                .WriteAsync(
                    path,
                    $"{path}.{Guid.NewGuid():N}{TempExtension}",
                    (stream, token) => stream.WriteAsync(rendition, token).AsTask(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException)
                                   && File.Exists(path))
        {
            // Already there, which is all this was for.
        }
    }

    public string ResolveTilePath(string thumbnailName)
    {
        string name = RenditionName.RequireSafeFileName(thumbnailName, nameof(thumbnailName));
        return Path.Combine(_workingFolder.ThumbnailsPath, Shard(name), name);
    }

    public string ResolvePreviewPath(string thumbnailName)
    {
        string name = RenditionName.RequireSafeFileName(thumbnailName, nameof(thumbnailName));
        string preview = Path.GetFileNameWithoutExtension(name)
                       + PreviewSuffix
                       + Path.GetExtension(name);
        return Path.Combine(_workingFolder.ThumbnailsPath, Shard(name), preview);
    }

    public bool Exists(string? thumbnailName) =>
        RenditionName.IsSafeFileName(thumbnailName)
        && File.Exists(ResolveTilePath(thumbnailName!))
        && File.Exists(ResolvePreviewPath(thumbnailName!));

    public DateTime? PreviewWrittenUtc(string? thumbnailName)
    {
        if (!RenditionName.IsSafeFileName(thumbnailName))
        {
            return null;
        }

        var file = new FileInfo(ResolvePreviewPath(thumbnailName!));
        return file.Exists ? file.LastWriteTimeUtc : null;
    }

    public bool TryDelete(string? thumbnailName)
    {
        if (string.IsNullOrWhiteSpace(thumbnailName))
        {
            return true;
        }

        if (!RenditionName.IsSafeFileName(thumbnailName))
        {
            return false;
        }

        // A single & rather than &&: the preview must still be attempted when the
        // tile refuses, or a locked tile would strand its preview forever.
        return Gone(ResolveTilePath(thumbnailName)) & Gone(ResolvePreviewPath(thumbnailName));
    }

    public IReadOnlyCollection<string> ListStoredNames(Action<string, Exception>? unreadable = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string root = _workingFolder.ThumbnailsPath;
        if (!Directory.Exists(root))
        {
            return names;
        }

        // Every file first, then paired in memory. Asking the disk about each
        // tile's preview in turn is what made this cost 2.0 to 3.8 s on 20,000
        // pictures, where the walk on its own takes under a tenth of that.
        //
        // Shard by shard, so a shard that goes part-way through - a detach
        // sweeping it, another copy of the app tidying up - costs that shard
        // and not the whole answer. Under thumbs\ only, never the working
        // folder root: the root may itself hold the user's pictures, which is
        // why IsAppOwned refuses it.
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string shard in Listed(root, Directory.EnumerateDirectories, unreadable))
        {
            files.UnionWith(Listed(
                shard,
                folder => Directory.EnumerateFiles(folder, '*' + Extension, s_shardListing),
                unreadable));
        }

        foreach (string file in files)
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
                continue;
            }

            // Both renditions where Exists would look for them, so the two
            // answers can never disagree about a picture.
            string name = stem + Extension;
            if (RenditionName.IsSafeFileName(name)
                && files.Contains(ResolveTilePath(name))
                && files.Contains(ResolvePreviewPath(name)))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// One folder's listing, read to the end, or nothing when it would not be
    /// read and the caller asked to be told rather than stopped.
    /// </summary>
    /// <remarks>
    /// Drained inside the try: entries arrive as the listing is read, so a
    /// folder removed part-way through fails here rather than in the middle of
    /// the caller's loop.
    /// </remarks>
    private static List<string> Listed(
        string folder,
        Func<string, IEnumerable<string>> list,
        Action<string, Exception>? unreadable)
    {
        try
        {
            return [.. list(folder)];
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException)
                                   && unreadable is not null)
        {
            unreadable(folder, ex);
            return [];
        }
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
