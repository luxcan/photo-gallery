using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Preferences;

/// <summary>
/// Remembers how far the user zoomed the grid, so it survives a restart.
/// </summary>
/// <remarks>
/// Writes only on a real change. A zoom arrives one wheel notch at a time and the
/// value loaded on open is applied through the same property, so without this
/// guard opening a library would write back what it had just read.
/// </remarks>
public sealed class SaveGalleryCellSizeHandler
{
    private readonly ILibraryIndex _index;

    public SaveGalleryCellSizeHandler(ILibraryIndex index) => _index = index;

    public async Task HandleAsync(
        double cellSize,
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings = await _index.GetSettingsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings.GalleryCellSize == cellSize)
        {
            return;
        }

        settings.GalleryCellSize = cellSize;
        await _index.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
