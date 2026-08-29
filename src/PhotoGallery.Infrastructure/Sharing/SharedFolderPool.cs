using System.IO.Compression;
using System.Text.Json;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// The cached pictures, pooled in the same folder the answers go through.
/// </summary>
/// <remarks>
/// Sharded exactly as the local store is, because the names are the same names -
/// a rendition is called after a hash of the original's bytes, and that is what
/// makes two machines able to pour into one folder without colliding.
///
/// <para>Every write goes through a temporary name and is renamed into place.
/// Two machines will fetch the same missing rendition at the same moment, and a
/// third must never read half a JPEG.</para>
/// </remarks>
public sealed class SharedFolderPool : IRenditionPool
{
    /// <summary>The pooled pictures. Sharded two deep, like the local store.</summary>
    public const string ThumbsFolder = "thumbs";

    /// <summary>One manifest per machine, saying what it has prepared.</summary>
    public const string FactsFolder = "facts";

    /// <summary>What a manifest is called, after the machine that wrote it.</summary>
    public const string Extension = ".facts.json.gz";

    private const string TempExtension = ".tmp";
    private const string PreviewSuffix = "-p";
    private const int ShardLength = 2;

    private static readonly JsonSerializerOptions s_json = DecisionSetFile.Shape;

    private readonly ILibraryIndex _index;

    public SharedFolderPool(ILibraryIndex index) => _index = index;

    public async Task<ExchangeReadiness> ReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(settings.SharedFolder))
        {
            return ExchangeReadiness.Not(
                "Choose a folder that every computer in the house can reach.");
        }

        return Directory.Exists(settings.SharedFolder)
            ? ExchangeReadiness.Ready
            : ExchangeReadiness.Not(
                $"That folder cannot be reached at the moment: {settings.SharedFolder}");
    }

    public async Task PublishAsync(
        PreparedSet mine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mine);

        string facts = Path.Combine(
            await RootAsync(cancellationToken).ConfigureAwait(false), FactsFolder);

        Directory.CreateDirectory(facts);

        string final = Path.Combine(facts, $"{mine.Machine.Id:D}{Extension}");
        string temporary = final + TempExtension;

        await using (var file = new FileStream(
            temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await using var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
            await JsonSerializer
                .SerializeAsync(gzip, mine, s_json, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporary, final, overwrite: true);
    }

    public async Task<IReadOnlyList<PreparedSet>> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        string root = await RootAsync(cancellationToken).ConfigureAwait(false);
        string facts = Path.Combine(root, FactsFolder);

        if (!Directory.Exists(facts))
        {
            return [];
        }

        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        string ours = $"{settings.MachineId:D}{Extension}";
        List<PreparedSet> sets = [];

        foreach (string path in Directory.EnumerateFiles(facts, "*" + Extension))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Our own manifest, which we wrote. Reading it back would cost a
            // megabyte of decompression to learn nothing.
            if (string.Equals(
                Path.GetFileName(path), ours, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await using var file = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await using var gzip = new GZipStream(file, CompressionMode.Decompress);

                if (await JsonSerializer
                        .DeserializeAsync<PreparedSet>(gzip, s_json, cancellationToken)
                        .ConfigureAwait(false) is PreparedSet set)
                {
                    sets.Add(set);
                }
            }
            catch (Exception ex) when (ex is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or InvalidDataException)
            {
                // One unreadable manifest must not cost the exchange every good
                // one. A machine writing at the moment this one read is the
                // ordinary case, and it comes good on the next run.
            }
        }

        return sets;
    }

    public async Task<IReadOnlyCollection<string>> NamesAsync(
        CancellationToken cancellationToken = default)
    {
        string thumbs = Path.Combine(
            await RootAsync(cancellationToken).ConfigureAwait(false), ThumbsFolder);

        if (!Directory.Exists(thumbs))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        // Only the tiles are counted, and only where the preview is beside them.
        // A name whose pair is half copied is a name nobody should be told the
        // pool has - the fetch would take the tile and find no preview, which is
        // the file the viewer opens.
        foreach (string path in Directory.EnumerateFiles(
            thumbs, "*.jpg", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = Path.GetFileName(path);

            if (name.Contains(PreviewSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            if (File.Exists(PreviewBeside(path)))
            {
                names.Add(name);
            }
        }

        return names;
    }

    public async Task<bool> PushAsync(
        string thumbnailName,
        string tilePath,
        string previewPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailName);

        if (!File.Exists(tilePath) || !File.Exists(previewPath))
        {
            return false;
        }

        string shard = await ShardPathAsync(thumbnailName, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(shard);

        string tile = Path.Combine(shard, thumbnailName);
        string preview = PreviewBeside(tile);

        // The preview first, here as everywhere: a listing counts a name only
        // when its preview is beside it, so writing the tile first would offer a
        // name whose preview has not arrived.
        return await CopyAsync(previewPath, preview, cancellationToken).ConfigureAwait(false)
            && await CopyAsync(tilePath, tile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PullAsync(
        string thumbnailName,
        string tilePath,
        string previewPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailName);

        string shard = await ShardPathAsync(thumbnailName, cancellationToken).ConfigureAwait(false);
        string tile = Path.Combine(shard, thumbnailName);
        string preview = PreviewBeside(tile);

        if (!File.Exists(tile) || !File.Exists(preview))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);

        // The preview before the tile. IThumbnailStore.Exists asks only about the
        // tile, so a copy interrupted between the two would leave a photograph
        // reporting itself complete with no preview - which is the file the
        // viewer opens and the face detector reads.
        return await CopyAsync(preview, previewPath, cancellationToken).ConfigureAwait(false)
            && await CopyAsync(tile, tilePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies one file through a temporary name and renames it into place.
    /// </summary>
    /// <remarks>
    /// Two machines will fetch the same missing rendition at the same moment.
    /// Renaming is what stops a third reading half a JPEG, and it is why a
    /// failure here answers false rather than throwing: the pair is written as a
    /// pair, and half of one is the only outcome that must never be recorded.
    /// </remarks>
    private static async Task<bool> CopyAsync(
        string from, string to, CancellationToken cancellationToken)
    {
        string temporary = to + TempExtension;

        try
        {
            await using (var source = new FileStream(
                from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            await using (var destination = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, to, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Forget(temporary);
            return false;
        }
        catch (OperationCanceledException)
        {
            // Stopped part-way. What was copied is copied and what was not is
            // picked up next time, which is what makes this resumable - but the
            // half-written temporary is nobody's.
            Forget(temporary);
            throw;
        }
    }

    private static void Forget(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover .tmp is untidy and harmless: nothing reads them, and the
            // next write to the same name replaces it.
        }
    }

    private static string PreviewBeside(string tilePath) =>
        Path.Combine(
            Path.GetDirectoryName(tilePath)!,
            Path.GetFileNameWithoutExtension(tilePath) + PreviewSuffix + Path.GetExtension(tilePath));

    /// <summary>The same two-deep sharding the local store uses, on the same names.</summary>
    private static string Shard(string thumbnailName)
    {
        string stem = Path.GetFileNameWithoutExtension(thumbnailName);
        return stem.Length < ShardLength ? stem.PadRight(ShardLength, '0') : stem[..ShardLength];
    }

    private async Task<string> ShardPathAsync(
        string thumbnailName, CancellationToken cancellationToken) =>
        Path.Combine(
            await RootAsync(cancellationToken).ConfigureAwait(false),
            ThumbsFolder,
            Shard(thumbnailName));

    private async Task<string> RootAsync(CancellationToken cancellationToken)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(settings.SharedFolder)
            ? throw new InvalidOperationException(
                "No shared folder has been chosen for this library.")
            : settings.SharedFolder;
    }
}
