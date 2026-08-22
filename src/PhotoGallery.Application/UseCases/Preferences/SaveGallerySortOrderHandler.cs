using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Preferences;

/// <summary>
/// Remembers which end of the library the grid starts at, so it survives a
/// restart.
/// </summary>
/// <remarks>
/// Writes only on a real change. The control binds straight to the gallery, so
/// applying a stored order while opening a library comes back through the same
/// property - without this guard, opening would write back what it had just read.
/// </remarks>
public sealed class SaveGallerySortOrderHandler
{
    private readonly ILibraryIndex _index;

    public SaveGallerySortOrderHandler(ILibraryIndex index) => _index = index;

    public async Task HandleAsync(
        GallerySortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings = await _index.GetSettingsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings.GallerySortOrder == sortOrder)
        {
            return;
        }

        settings.GallerySortOrder = sortOrder;
        await _index.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
