using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Updates;

/// <summary>
/// Installs a published version over the running one, when somebody asks.
/// </summary>
/// <remarks>
/// Never on its own: the check offers, a person presses Update, and only then
/// does anything leave the internet. Fetching and proving the file comes first
/// and touches nothing installed, so a slow line or a Stop costs nothing but
/// the download.
/// </remarks>
public sealed class UpdateAppHandler
{
    private readonly IAppInstaller _installer;

    public UpdateAppHandler(IAppInstaller installer) => _installer = installer;

    /// <summary>Whether a release can be installed here, rather than only read about.</summary>
    public bool CanInstall(PublishedRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Download is not null && _installer.CanReplaceItself;
    }

    /// <summary>
    /// Downloads, proves, installs and starts the new version. On return the
    /// caller closes this one.
    /// </summary>
    /// <exception cref="UpdateFailedException">
    /// Nothing was installed, and the message says why in a sentence.
    /// </exception>
    public async Task InstallAsync(
        PublishedRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.Download is not ReleaseDownload download || !_installer.CanReplaceItself)
        {
            throw new UpdateFailedException(
                "This copy of Photo Gallery cannot update itself. Download it from the release page.");
        }

        string staged = await _installer
            .DownloadAsync(download, progress, cancellationToken)
            .ConfigureAwait(false);

        // Past this point there is no Stop. The swap is three renames and a
        // start, each undone if a later one fails; interrupting it between two
        // of them is the one way to leave somebody without an app.
        _installer.InstallAndStart(staged);
    }
}
