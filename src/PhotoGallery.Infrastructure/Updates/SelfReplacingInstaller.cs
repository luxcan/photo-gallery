using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Updates;

/// <inheritdoc cref="IAppInstaller"/>
/// <remarks>
/// Downloads go to <c>%LOCALAPPDATA%\PhotoGallery\updates</c>, never into the
/// install folder: the install folder may be on the NAS, where a half-written
/// file is somebody else's problem too. Only a file already proved by its digest
/// is ever copied next to the running executable.
/// </remarks>
public sealed class SelfReplacingInstaller : IAppInstaller
{
    /// <summary>The argument the new version is started with, and the id it waits on.</summary>
    public const string AfterUpdateArgument = "--after-update";

    /// <summary>
    /// The suffix the incoming copy carries until it takes the running one's
    /// name. Not <c>.exe</c>, so nothing can start it by mistake.
    /// </summary>
    private const string IncomingSuffix = ".new";

    private const string OldSuffix = ".old.exe";

    private readonly HttpClient _http;
    private readonly string? _running;
    private readonly string _staging;

    /// <param name="http">For tests; a real one otherwise.</param>
    /// <param name="runningPath">
    /// The executable to replace. The process's own unless a test names another.
    /// </param>
    /// <param name="stagingFolder">Where downloads wait before they are installed.</param>
    public SelfReplacingInstaller(
        HttpClient? http = null, string? runningPath = null, string? stagingFolder = null)
    {
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            // GitHub refuses a request that does not say who is asking.
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("PhotoGallery", "1.0"));
        }

        _running = runningPath ?? Environment.ProcessPath;
        _staging = stagingFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoGallery",
            "updates");
    }

    public bool CanReplaceItself =>
        _running is not null
        && string.Equals(Path.GetExtension(_running), ".exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(_running)

        // A build from source is an app host beside its own assembly; the
        // published single file has the assembly inside it.
        && !File.Exists(Path.ChangeExtension(_running, ".dll"));

    public async Task<string> DownloadAsync(
        ReleaseDownload download,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(download);

        string expected = await ExpectedDigestAsync(download, cancellationToken)
            .ConfigureAwait(false);

        Directory.CreateDirectory(_staging);
        string staged = Path.Combine(_staging, $"PhotoGallery-{expected[..12]}.exe");
        string partial = staged + ".partial";

        try
        {
            string actual = await FetchAsync(
                    download.Address, download.Length, partial, progress, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateFailedException(
                    "The download was damaged, so it was not installed. Try again later.");
            }

            File.Move(partial, staged, overwrite: true);
            return staged;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
                                      or UnauthorizedAccessException)
        {
            throw new UpdateFailedException(
                "Could not download the update. Check the internet connection and try again.",
                ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateFailedException(
                "Could not download the update. Check the internet connection and try again.",
                ex);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    public void InstallAndStart(string staged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staged);

        if (!CanReplaceItself || _running is null)
        {
            throw new UpdateFailedException(
                "This copy of Photo Gallery cannot update itself. Download it from the release page.");
        }

        string incoming = _running + IncomingSuffix;
        string old = OldPath(_running);

        Replace(staged, _running, incoming, old);

        try
        {
            using Process? started = Process.Start(new ProcessStartInfo
            {
                FileName = _running,
                Arguments = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{AfterUpdateArgument} {Environment.ProcessId}"),
                WorkingDirectory = Path.GetDirectoryName(_running)!,
                UseShellExecute = false,
            });

            if (started is null)
            {
                throw new InvalidOperationException("the new version did not start");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or System.ComponentModel.Win32Exception
                                      or IOException)
        {
            PutBack(_running, old);
            throw new UpdateFailedException(
                "The new version would not start, so the old one was put back.", ex);
        }

        TryDelete(staged);
    }

    public void CleanUp()
    {
        if (_running is not null)
        {
            TryDelete(OldPath(_running));
            TryDelete(_running + IncomingSuffix);
        }

        if (!Directory.Exists(_staging))
        {
            return;
        }

        foreach (string leftover in Directory.EnumerateFiles(_staging))
        {
            TryDelete(leftover);
        }
    }

    /// <summary>
    /// The swap itself: copy in, rename the running file aside, rename the copy
    /// into its place. Each step is undone if the next one fails.
    /// </summary>
    /// <remarks>
    /// Internal so the swap can be tried against a file held open the way a
    /// running executable is, without starting anything.
    /// </remarks>
    internal static void Replace(string staged, string running, string incoming, string old)
    {
        try
        {
            File.Copy(staged, incoming, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(incoming);
            throw new UpdateFailedException(
                "Could not write the update next to Photo Gallery. Check that its folder can be written to.",
                ex);
        }

        try
        {
            // A previous version still here is one whose cleanup has not run;
            // it has to go before the running one can take its name.
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(running, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(incoming);
            throw new UpdateFailedException(
                "Could not replace Photo Gallery. If another computer is running it from the same folder, close it there and try again.",
                ex);
        }

        try
        {
            File.Move(incoming, running);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PutBack(running, old);
            TryDelete(incoming);
            throw new UpdateFailedException(
                "Could not replace Photo Gallery, so the old version was put back.", ex);
        }
    }

    /// <summary>Where the running executable is kept while the new one takes its name.</summary>
    internal static string OldPath(string running) =>
        Path.Combine(
            Path.GetDirectoryName(running)!,
            Path.GetFileNameWithoutExtension(running) + OldSuffix);

    /// <summary>
    /// The digest the release states, read directly or from the file beside it.
    /// </summary>
    private async Task<string> ExpectedDigestAsync(
        ReleaseDownload download, CancellationToken cancellationToken)
    {
        if (Hex(download.Sha256) is string stated)
        {
            return stated;
        }

        if (download.Sha256Address is Uri address)
        {
            try
            {
                string text = await _http
                    .GetStringAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                // "<hex>  PhotoGallery.exe", or the hex alone.
                if (Hex(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()) is string read)
                {
                    return read;
                }
            }
            catch (HttpRequestException ex)
            {
                throw new UpdateFailedException(
                    "Could not download the update. Check the internet connection and try again.",
                    ex);
            }
        }

        throw new UpdateFailedException(
            "This release cannot be checked, so it was not installed. Download it from the release page.");
    }

    /// <summary>
    /// Streams the file to disk, hashing as it goes, and answers the digest of
    /// what arrived.
    /// </summary>
    private async Task<string> FetchAsync(
        Uri address,
        long length,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage answer = await _http
            .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        answer.EnsureSuccessStatusCode();

        long total = answer.Content.Headers.ContentLength ?? length;

        await using Stream body = await answer.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long received = 0;

        await using (FileStream file = File.Create(destination))
        {
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);

                received += read;
                if (total > 0)
                {
                    progress?.Report(Math.Min(1d, (double)received / total));
                }
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Gives the previous version its name back, or says plainly where it is.
    /// </summary>
    /// <exception cref="UpdateFailedException">
    /// It could not be renamed back: the one outcome that needs a person.
    /// </exception>
    private static void PutBack(string running, string old)
    {
        try
        {
            if (File.Exists(running))
            {
                File.Delete(running);
            }

            File.Move(old, running);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateFailedException(
                $"The update failed and the old version could not be put back. Rename "
                + $"{Path.GetFileName(old)} to {Path.GetFileName(running)} in "
                + $"{Path.GetDirectoryName(running)}.",
                ex);
        }
    }

    /// <summary>Lower-case hex of a 64-character SHA-256, or null for anything else.</summary>
    internal static string? Hex(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string digest = value.Trim();
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            digest = digest["sha256:".Length..];
        }

        return digest.Length == 64 && digest.All(Uri.IsHexDigit)
            ? digest.ToLowerInvariant()
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still held - a previous version taking a moment to close. The next
            // start tries again.
        }
    }
}
