using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
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

    /// <summary>The face vectors, one file per machine.</summary>
    public const string VectorsFolder = "vectors";

    /// <summary>What a machine's vectors are called.</summary>
    public const string VectorsExtension = ".faces.json.gz";

    /// <summary>
    /// Where this library remembers the faces it last left in the folder, in its
    /// own working folder.
    /// </summary>
    /// <remarks>
    /// A note of what was written, not a fact about the library, and safe to
    /// lose: without it the next scan writes the faces once more and writes the
    /// note again. So it sits beside the index as a file rather than in it as a
    /// column, and nothing in the shared folder changes to keep it.
    /// </remarks>
    public const string GivenFacesFile = "faces-given.json";

    private const string TempExtension = ".tmp";
    private const string PreviewSuffix = "-p";
    private const int ShardLength = 2;

    private static readonly JsonSerializerOptions s_json = DecisionSetFile.Shape;

    private readonly ILibraryIndex _index;
    private readonly IWorkingFolder _working;

    public SharedFolderPool(ILibraryIndex index, IWorkingFolder working)
    {
        _index = index;
        _working = working;
    }

    public async Task<ExchangeReadiness> ReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        string? folder = settings.SharedFolder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            return ExchangeReadiness.Not(
                "Choose a folder that every computer in the house can reach.");
        }

        // Off the caller's thread, because this is the call that blocks: an
        // address nothing answers holds Directory.Exists for as long as the
        // operating system takes to give up on it, and a scan asking has to be
        // able to stop waiting before then.
        bool there = await Task
            .Run(() => Directory.Exists(folder), CancellationToken.None)
            .ConfigureAwait(false);

        return there
            ? ExchangeReadiness.Ready
            : ExchangeReadiness.Not(
                $"That folder cannot be reached at the moment: {folder}");
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

    /// <inheritdoc/>
    /// <remarks>
    /// Whether they changed is judged on a digest of everything in the file but
    /// the moment it was written, against the digest noted beside the index the
    /// last time it was written - and only while that file is still where it was
    /// written. A folder chosen afresh, or a file somebody deleted, is written
    /// again whatever the note says.
    /// </remarks>
    public async Task<bool> PublishFacesAsync(
        FaceSet mine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mine);

        string vectors = Path.Combine(
            await RootAsync(cancellationToken).ConfigureAwait(false), VectorsFolder);

        string final = Path.Combine(vectors, $"{mine.Machine.Id:D}{VectorsExtension}");
        string digest = await DigestAsync(mine, cancellationToken).ConfigureAwait(false);

        if (Remembered() is { } last
            && string.Equals(last.File, final, StringComparison.OrdinalIgnoreCase)
            && string.Equals(last.Digest, digest, StringComparison.Ordinal)
            && File.Exists(final))
        {
            return false;
        }

        Directory.CreateDirectory(vectors);

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
        Remember(new GivenFaces(final, digest));

        return true;
    }

    /// <summary>
    /// The faces as they would be written, less the moment of writing, reduced
    /// to one digest.
    /// </summary>
    /// <remarks>
    /// Hashed as it is serialised rather than serialised and then hashed, so the
    /// tens of megabytes of text never exist at once. The moment is left out
    /// because it changes on every write and says nothing about the faces.
    /// </remarks>
    private static async Task<string> DigestAsync(
        FaceSet mine, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();

        await using (var hashing = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
        {
            await JsonSerializer
                .SerializeAsync(hashing, mine with { WrittenUtc = default }, s_json, cancellationToken)
                .ConfigureAwait(false);
        }

        return Convert.ToHexString(sha.Hash!);
    }

    private string GivenFacesPath => Path.Combine(_working.Root, GivenFacesFile);

    /// <summary>The note left by the last write, or null where there is none to read.</summary>
    private GivenFaces? Remembered()
    {
        try
        {
            return JsonSerializer.Deserialize<GivenFaces>(File.ReadAllText(GivenFacesPath));
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or JsonException)
        {
            // No note, or one half written: the faces are written again, which
            // is what happened on every scan before the note existed.
            return null;
        }
    }

    /// <summary>Notes what was just written, so the next scan can leave it alone.</summary>
    private void Remember(GivenFaces given)
    {
        try
        {
            File.WriteAllText(GivenFacesPath, JsonSerializer.Serialize(given));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The faces are in the folder, which is what this phase is for. A
            // note that could not be kept costs the next scan one more write of
            // the same file, and failing the phase over it would cost the file.
        }
    }

    /// <summary>Which faces file this library last wrote, and what was in it.</summary>
    private sealed record GivenFaces(string File, string Digest);

    public async Task<IReadOnlyList<FaceSet>> FetchFacesAsync(
        CancellationToken cancellationToken = default)
    {
        string vectors = Path.Combine(
            await RootAsync(cancellationToken).ConfigureAwait(false), VectorsFolder);

        if (!Directory.Exists(vectors))
        {
            return [];
        }

        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        string ours = $"{settings.MachineId:D}{VectorsExtension}";
        List<FaceSet> sets = [];

        foreach (string path in Directory.EnumerateFiles(vectors, "*" + VectorsExtension))
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                        .DeserializeAsync<FaceSet>(gzip, s_json, cancellationToken)
                        .ConfigureAwait(false) is FaceSet set)
                {
                    sets.Add(set);
                }
            }
            catch (Exception ex) when (ex is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or InvalidDataException)
            {
                // One unreadable file must not cost the exchange every good one.
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

        List<string> tiles = [];
        HashSet<string> previews = new(StringComparer.OrdinalIgnoreCase);

        // Only the tiles are counted, and only where the preview is beside them.
        // A name whose pair is half copied is a name nobody should be told the
        // pool has - the fetch would take the tile and find no preview, which is
        // the file the viewer opens.
        //
        // Both halves from the one listing, because every scan asks this now.
        // Asking the folder about each tile's preview in turn was twenty
        // thousand more round trips to a drive across the network, to learn
        // what the listing had already said.
        foreach (string path in Directory.EnumerateFiles(
            thumbs, "*.jpg", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = Path.GetFileName(path);

            if (name.Contains(PreviewSuffix, StringComparison.Ordinal))
            {
                previews.Add(path);
            }
            else if (RenditionName.IsSafeFileName(name))
            {
                tiles.Add(path);
            }
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (string tile in tiles)
        {
            if (previews.Contains(PreviewBeside(tile)))
            {
                names.Add(Path.GetFileName(tile));
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

        if (!RenditionName.IsSafeFileName(thumbnailName)
            || !File.Exists(tilePath)
            || !File.Exists(previewPath))
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

        if (!RenditionName.IsSafeFileName(thumbnailName))
        {
            return false;
        }

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
                "No sync folder has been chosen for this library.")
            : settings.SharedFolder;
    }
}
