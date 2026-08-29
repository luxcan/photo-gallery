using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Nominates the folder every computer in the house writes its answers into.
/// </summary>
/// <remarks>
/// Once, and then never again - which is why it is worth refusing the wrong
/// answer loudly rather than discovering it later.
///
/// <para><strong>It must not sit inside a photo source, and no photo source may
/// sit inside it.</strong> The pooled pictures are ordinary <c>.jpg</c> files in
/// a folder tree; a scan would index them as photographs, and the library would
/// grow a second copy of itself every time anybody pressed Refresh. The check
/// runs both ways here and again where a source is added, because either half on
/// its own leaves the hole the other was closing.</para>
/// </remarks>
public sealed class SetSharedFolderHandler
{
    private readonly ILibraryIndex _index;
    private readonly IWorkingFolder _workingFolder;

    public SetSharedFolderHandler(ILibraryIndex index, IWorkingFolder workingFolder)
    {
        _index = index;
        _workingFolder = workingFolder;
    }

    public async Task HandleAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string folder = FolderOverlap.Normalise(path);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"The folder is not reachable: {folder}");
        }

        if (_workingFolder.IsAppOwned(folder))
        {
            throw new InvalidOperationException(
                "That folder belongs to Photo Gallery itself. "
              + "Choose a folder the other computers can reach instead.");
        }

        IReadOnlyList<PhotoSource> sources =
            await _index.GetSourcesAsync(cancellationToken).ConfigureAwait(false);

        foreach (PhotoSource source in sources)
        {
            if (FolderOverlap.Any(folder, source.Path))
            {
                throw new InvalidOperationException(
                    $"That folder overlaps the photos in {source.Path}. "
                  + "Sharing writes files, and a scan would index them as photographs. "
                  + "Choose a folder outside your photos.");
            }
        }

        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        settings.SharedFolder = folder;

        await _index.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops sharing, without forgetting anything already merged.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        settings.SharedFolder = null;

        await _index.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
