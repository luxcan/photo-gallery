using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Scanning;

/// <summary>
/// Indexes one photo source: which media files exist, how big they are and when
/// they changed.
/// </summary>
/// <remarks>
/// Deliberately cheap. This pass never opens a file - it works purely from
/// directory metadata, which on a 17,000-file share takes seconds rather than
/// the hour that reading the bytes costs. Thumbnails, EXIF and hashes are
/// separate passes over the rows this one produces, so the library becomes
/// browsable long before the expensive work finishes.
///
/// Re-scanning is near-free: a file whose size and timestamp are unchanged is
/// skipped without being touched, so the second scan of an untouched source
/// costs little more than the directory walk.
/// </remarks>
public sealed class ScanPhotoSourceHandler
{
    private const int BatchSize = 500;

    private readonly ILibraryIndex _index;
    private readonly IAssetRepository _assets;
    private readonly IMediaFileWalker _walker;
    private readonly IThumbnailStore _thumbnails;

    public ScanPhotoSourceHandler(
        ILibraryIndex index,
        IAssetRepository assets,
        IMediaFileWalker walker,
        IThumbnailStore thumbnails)
    {
        _index = index;
        _assets = assets;
        _walker = walker;
        _thumbnails = thumbnails;
    }

    public async Task<ScanResult> HandleAsync(
        int photoSourceId,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Setup runs uncancelled: these are two small queries, and a token that
        // is already cancelled should yield a cancelled result rather than
        // throwing out of the handler before it can report anything.
        IReadOnlyList<PhotoSource> sources = await _index.GetSourcesAsync(CancellationToken.None)
            .ConfigureAwait(false);
        PhotoSource source = sources.FirstOrDefault(s => s.Id == photoSourceId)
            ?? throw new InvalidOperationException($"No photo source with id {photoSourceId}.");

        var stopwatch = Stopwatch.StartNew();

        // One timestamp for the whole pass, so everything this scan found reads
        // as having arrived together - which it did.
        DateTime startedUtc = DateTime.UtcNow;

        // One query for the whole source; the walk then decides per file in
        // memory instead of hitting the database 17,000 times.
        Dictionary<string, AssetSignature> known =
            await _assets.GetSignaturesAsync(photoSourceId, CancellationToken.None)
                .ConfigureAwait(false);
        var seenPaths = new HashSet<string>(known.Count, StringComparer.OrdinalIgnoreCase);

        MediaWalk walk = _walker.Walk(source.Path, cancellationToken);
        if (walk.RootUnreadable)
        {
            // Nothing about this source can be proved while it cannot be read, so
            // the pass stops here rather than concluding that every one of its
            // files has gone. That conclusion once emptied a whole library.
            stopwatch.Stop();
            return ScanResult.Unavailable(
                photoSourceId, source.Path, known.Count, stopwatch.Elapsed);
        }

        var toAdd = new List<Asset>(BatchSize);
        var toUpdate = new List<Asset>(BatchSize);
        var toStamp = new List<(int AssetId, DateTime CreatedUtc)>();

        // Renditions belonging to files that have since changed. Named after the
        // picture's content, so the new bytes will be written under a new name
        // and these would otherwise stay on disk with nothing referring to them.
        var superseded = new List<string>();
        int added = 0, updated = 0, unchanged = 0, seen = 0;
        string reportedFolder = string.Empty;
        bool cancelled = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ScannedFile file in walk.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AssetKind kind = MediaFileTypes.Classify(file.RelativePath);
                if (kind == AssetKind.Unknown)
                {
                    continue;
                }

                seen++;
                seenPaths.Add(file.RelativePath);

                if (known.TryGetValue(file.RelativePath, out AssetSignature signature))
                {
                    if (signature.Matches(file.Length, file.ModifiedUtc, file.CreatedUtc))
                    {
                        // Rows indexed before creation dates were recorded hold
                        // no value for one. The file has not changed, so it is
                        // stamped rather than rebuilt - putting it through the
                        // update path would discard its thumbnail and hashes.
                        if (signature.CreatedUtc == default)
                        {
                            toStamp.Add((signature.AssetId, file.CreatedUtc));
                        }

                        unchanged++;
                        continue;
                    }

                    // Replaced or edited in place: keep the row, refresh the
                    // facts, and clear derived data so later passes redo it.
                    if (signature.ThumbnailName is { Length: > 0 } stale)
                    {
                        superseded.Add(stale);
                    }

                    toUpdate.Add(new Asset
                    {
                        Id = signature.AssetId,
                        PhotoSourceId = photoSourceId,
                        RelativePath = file.RelativePath,
                        Length = file.Length,
                        ModifiedUtc = file.ModifiedUtc,
                        CreatedUtc = file.CreatedUtc,
                        Kind = kind,
                        Status = StatusFor(kind),
                    });
                    updated++;
                }
                else
                {
                    toAdd.Add(new Asset
                    {
                        PhotoSourceId = photoSourceId,
                        RelativePath = file.RelativePath,
                        Length = file.Length,
                        ModifiedUtc = file.ModifiedUtc,
                        CreatedUtc = file.CreatedUtc,
                        Kind = kind,
                        IndexedUtc = startedUtc,
                        Status = StatusFor(kind),
                    });
                    added++;
                }

                if (toAdd.Count >= BatchSize)
                {
                    await _assets.AddRangeAsync(toAdd, cancellationToken).ConfigureAwait(false);
                    toAdd.Clear();
                }

                if (toUpdate.Count >= BatchSize)
                {
                    await _assets.UpdateRangeAsync(toUpdate, cancellationToken).ConfigureAwait(false);
                    toUpdate.Clear();
                }

                // Reported when the folder changes as well as every 250 files,
                // because a folder holding fewer than that would otherwise never
                // be named - and on this library most of them hold fewer.
                string folder = FolderOf(file.RelativePath);
                if (seen % 250 == 0 || !string.Equals(folder, reportedFolder, StringComparison.Ordinal))
                {
                    reportedFolder = folder;
                    progress?.Report(
                        new ScanProgress(source.Path, seen, added, updated, unchanged, folder));
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        if (toAdd.Count > 0)
        {
            await _assets.AddRangeAsync(toAdd, CancellationToken.None).ConfigureAwait(false);
        }

        if (toUpdate.Count > 0)
        {
            await _assets.UpdateRangeAsync(toUpdate, CancellationToken.None).ConfigureAwait(false);
        }

        if (toStamp.Count > 0)
        {
            await _assets.SetCreatedDatesAsync(toStamp, CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Asked after the rows above were rewritten, so a name that still comes
        // back genuinely belongs to some other picture and must be left alone.
        await ReclaimSupersededAsync(superseded).ConfigureAwait(false);

        // Only a completed walk proves a file is gone. A cancelled one has simply
        // not reached it yet, and one that could not read a folder was never told
        // what is inside it. Neither may remove what it did not see.
        string[] unreadable = RelativePrefixes(walk);
        int removed = 0, kept = 0;
        if (!cancelled)
        {
            var missing = new List<int>();
            foreach (KeyValuePair<string, AssetSignature> entry in known)
            {
                if (seenPaths.Contains(entry.Key))
                {
                    continue;
                }

                if (IsUnderUnreadableFolder(entry.Key, unreadable))
                {
                    kept++;
                    continue;
                }

                // A copy set aside as redundant is meant to be absent. Its row is
                // the only thing that knows how to put it back, so a scan that
                // took it away would make the quarantine a one-way door.
                if (entry.Value.IsQuarantined)
                {
                    kept++;
                    continue;
                }

                missing.Add(entry.Value.AssetId);
            }

            if (missing.Count > 0)
            {
                await _assets.RemoveAsync(missing, CancellationToken.None).ConfigureAwait(false);
                removed = missing.Count;
            }

            // Stamped even when a folder went unread: the scan did happen, and
            // what it could not reach is reported separately. Withholding it
            // would leave the row saying "Never" for as long as one folder stays
            // locked.
            source.LastScanUtc = DateTime.UtcNow;
            await _index.UpdateSourceAsync(source, CancellationToken.None).ConfigureAwait(false);
        }

        stopwatch.Stop();
        progress?.Report(new ScanProgress(source.Path, seen, added, updated, unchanged));

        return new ScanResult(
            photoSourceId, source.Path, added, updated, removed, unchanged,
            stopwatch.Elapsed, cancelled, WasUnavailable: false,
            FoldersNotRead: unreadable.Length, Kept: kept);
    }

    /// <summary>
    /// Where the crawl parks each kind, and the one line the whole photo/video
    /// split turns on.
    /// </summary>
    /// <remarks>
    /// A new or changed picture is <see cref="AssetStatus.Pending"/> and the
    /// preparing pass takes it from there. A video is
    /// <see cref="AssetStatus.Skipped"/> - not because there is nothing to make
    /// from it, which stopped being true when the keyframe pass landed, but
    /// because that pass is a separate and much longer one somebody has to
    /// choose to start. Left merely pending, 4,743 clips would sit permanently
    /// outstanding in a pass that cannot decode any of them.
    /// </remarks>
    private static AssetStatus StatusFor(AssetKind kind) =>
        kind == AssetKind.Video ? AssetStatus.Skipped : AssetStatus.Pending;

    /// <summary>
    /// Deletes the renditions of files that have changed, sparing any that
    /// another picture still points at.
    /// </summary>
    private async Task ReclaimSupersededAsync(List<string> superseded)
    {
        if (superseded.Count == 0)
        {
            return;
        }

        HashSet<string> stillUsed = await _assets
            .GetReferencedThumbnailNamesAsync(superseded, CancellationToken.None)
            .ConfigureAwait(false);

        foreach (string name in superseded)
        {
            if (!stillUsed.Contains(name))
            {
                _thumbnails.TryDelete(name);
            }
        }

        _thumbnails.RemoveEmptyShards();
    }

    /// <summary>
    /// The unreadable folders, expressed the way the index stores paths: relative
    /// to the source root, one separator, and with a trailing one so that "2016"
    /// cannot match "2016 Bali".
    /// </summary>
    private static string[] RelativePrefixes(MediaWalk walk) =>
        [.. walk.UnreadableFolders
            .Select(folder => Path.GetRelativePath(walk.Root, folder))
            .Where(relative => relative != "."
                && !relative.StartsWith("..", StringComparison.Ordinal))
            .Select(relative => Flatten(relative) + '/')];

    private static bool IsUnderUnreadableFolder(string relativePath, string[] prefixes)
    {
        if (prefixes.Length == 0)
        {
            return false;
        }

        string candidate = Flatten(relativePath);
        foreach (string prefix in prefixes)
        {
            // Case-insensitively, to match how the index is keyed and how Windows
            // itself compares paths.
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Flatten(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>
    /// The folder a relative path sits in, or empty at the source's own root.
    /// </summary>
    private static string FolderOf(string relativePath)
    {
        int cut = relativePath.LastIndexOfAny(s_separators);
        return cut < 0 ? string.Empty : relativePath[..cut];
    }

    private static readonly char[] s_separators = ['\\', '/'];
}
