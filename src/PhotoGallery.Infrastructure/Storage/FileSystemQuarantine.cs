using System.Security.Cryptography;
using System.Text.Json;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Storage;

/// <inheritdoc cref="IQuarantineStore"/>
public sealed class FileSystemQuarantine : IQuarantineStore
{
    /// <summary>The name a copy carries until it has been checked.</summary>
    private const string PartialSuffix = ".partial";

    private static readonly JsonSerializerOptions s_manifestFormat =
        new() { WriteIndented = true };

    private readonly IWorkingFolder _workingFolder;

    public FileSystemQuarantine(IWorkingFolder workingFolder) => _workingFolder = workingFolder;

    public string PathFor(int photoSourceId, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return Path.Combine(
            _workingFolder.QuarantinePath,
            photoSourceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            relativePath);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Copy, verify the length, then delete - rather than <c>File.Move</c>.
    /// The library is on a network share and the quarantine is on a local disk,
    /// so every move here crosses a volume and is a copy and a delete whichever
    /// way it is written. Doing it explicitly is what allows the original to be
    /// left alone when the copy does not arrive intact.
    /// </remarks>
    public async Task<bool> PutAsync(
        string originalFullPath,
        int photoSourceId,
        string relativePath,
        string? contentHash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFullPath);

        string destination = PathFor(photoSourceId, relativePath);

        // Written under a working name so an interrupted copy cannot be mistaken
        // for a file safely set aside.
        string partial = destination + PartialSuffix;

        try
        {
            if (!File.Exists(originalFullPath))
            {
                // Already gone by some other route. Nothing to move, and saying
                // it moved would have the row claim a file that is not there.
                return File.Exists(destination);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            long expected = new FileInfo(originalFullPath).Length;

            await CopyAsync(originalFullPath, partial, cancellationToken).ConfigureAwait(false);

            if (!Arrived(partial, expected, contentHash))
            {
                return false;
            }

            File.Move(partial, destination, overwrite: true);

            if (Discard(originalFullPath))
            {
                return true;
            }

            // The copy is intact but the original will not go - it is open in
            // another program, or read-only. There are two of it now, and the
            // row must not be told the library's one has moved. Put the
            // quarantine back as it was and report the refusal, so the set stays
            // on the screen with something left to do.
            Discard(destination);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            // On every path, including a cancellation on its way out. A working
            // copy left behind is invisible to restoring, unnamed by the
            // manifest, and nothing ever comes back for it.
            Discard(partial);
        }
    }

    /// <summary>
    /// Whether the copy is the file it was meant to be.
    /// </summary>
    /// <remarks>
    /// Length first because it is free and rejects a truncated copy outright.
    /// The digest is what makes deleting the original safe: the library sits on
    /// a network share, and a transfer can corrupt bytes without changing how
    /// many of them there are.
    /// </remarks>
    private static bool Arrived(string path, long expectedLength, string? expectedHash)
    {
        if (new FileInfo(path).Length != expectedLength)
        {
            return false;
        }

        if (string.IsNullOrEmpty(expectedHash))
        {
            return true;
        }

        using FileStream stream = File.OpenRead(path);
        return string.Equals(
            Convert.ToHexStringLower(SHA256.HashData(stream)),
            expectedHash,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes a file if it can, and says whether it is gone.</summary>
    private static bool Discard(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task<bool> TakeBackAsync(
        string originalFullPath,
        int photoSourceId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFullPath);

        string source = PathFor(photoSourceId, relativePath);

        try
        {
            if (File.Exists(originalFullPath))
            {
                // Already home - somebody put it back by hand, or a previous
                // restore got as far as the file and not as far as the row.
                if (File.Exists(source))
                {
                    File.Delete(source);
                }

                return true;
            }

            if (!File.Exists(source))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(originalFullPath)!);

            long expected = new FileInfo(source).Length;
            await CopyAsync(source, originalFullPath, cancellationToken).ConfigureAwait(false);

            if (new FileInfo(originalFullPath).Length != expected)
            {
                File.Delete(originalFullPath);
                return false;
            }

            File.Delete(source);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Appended to rather than replaced, and named for the day: a person opening
    /// this folder months later wants the whole history of what was set aside,
    /// not only the last batch.
    /// </remarks>
    public async Task WriteManifestAsync(
        IReadOnlyList<QuarantinedCopy> copies, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copies);

        if (copies.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_workingFolder.QuarantinePath);

            string path = Path.Combine(
                _workingFolder.QuarantinePath,
                $"set-aside-{DateTime.Now:yyyy-MM-dd}.json");

            List<ManifestEntry> entries = File.Exists(path)
                ? JsonSerializer.Deserialize<List<ManifestEntry>>(
                      await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false))
                  ?? []
                : [];

            entries.AddRange(copies.Select(copy => new ManifestEntry(
                copy.QuarantinedUtc,
                copy.OriginalFullPath,
                PathFor(copy.PhotoSourceId, copy.RelativePath),
                copy.Length)));

            await File.WriteAllTextAsync(
                    path,
                    JsonSerializer.Serialize(entries, s_manifestFormat),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or JsonException)
        {
            // A manifest that could not be written must not undo files that were
            // moved successfully. Restoring reads the layout, not this.
        }
    }

    private static async Task CopyAsync(
        string from, string to, CancellationToken cancellationToken)
    {
        await using FileStream source = File.Open(
            from, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });

        await using FileStream destination = File.Open(
            to, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });

        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private sealed record ManifestEntry(
        DateTime SetAsideUtc, string CameFrom, string NowAt, long Length);
}
