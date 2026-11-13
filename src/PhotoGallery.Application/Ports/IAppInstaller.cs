namespace PhotoGallery.Application.Ports;

/// <summary>
/// Replaces the running executable with a newer one, and starts it.
/// </summary>
/// <remarks>
/// A running program cannot overwrite its own file, but Windows lets it rename
/// it. So the new file is copied beside the old one, the old one is renamed
/// aside, the new one takes its name, and the new one is started. Every step
/// before the last can be undone, and is when a later one fails: a failed update
/// leaves the version that was running, never half of one.
/// </remarks>
public interface IAppInstaller
{
    /// <summary>
    /// Whether this is a build that can replace itself.
    /// </summary>
    /// <remarks>
    /// Only the published single-file executable. A build run from Visual
    /// Studio or <c>dotnet run</c> is a folder of assemblies, and swapping one
    /// file in it for a whole application would break it.
    /// </remarks>
    bool CanReplaceItself { get; }

    /// <summary>
    /// Fetches the executable and proves it against the release's digest.
    /// </summary>
    /// <returns>Where the proved file has been put, outside the install folder.</returns>
    /// <exception cref="UpdateFailedException">
    /// It could not be fetched, or what arrived is not what was published.
    /// </exception>
    Task<string> DownloadAsync(
        ReleaseDownload download,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the fetched file in place of the running one and starts it, handing
    /// it this process's id so it waits for this one to close first.
    /// </summary>
    /// <exception cref="UpdateFailedException">
    /// Nothing was replaced: the running version is exactly as it was.
    /// </exception>
    void InstallAndStart(string staged);

    /// <summary>
    /// Removes what an update leaves behind: the previous executable, a copy that
    /// never took its place, and downloads already installed.
    /// </summary>
    /// <remarks>
    /// Best effort. A file still held - the previous version taking a moment to
    /// close - is left for the next start, and costs nothing until then.
    /// </remarks>
    void CleanUp();
}
