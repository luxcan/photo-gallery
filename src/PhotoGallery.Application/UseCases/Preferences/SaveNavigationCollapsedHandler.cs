using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Preferences;

/// <summary>
/// Remembers that the user folded the side nav, so it survives a restart.
/// </summary>
/// <remarks>
/// Writes only on a real change, as its siblings do: the stored value is applied
/// again while the library opens, and without the guard opening a library would
/// write back what it had just read.
/// </remarks>
public sealed class SaveNavigationCollapsedHandler
{
    private readonly ILibraryIndex _index;

    public SaveNavigationCollapsedHandler(ILibraryIndex index) => _index = index;

    public async Task HandleAsync(
        bool collapsed,
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings = await _index.GetSettingsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings.NavigationCollapsed == collapsed)
        {
            return;
        }

        settings.NavigationCollapsed = collapsed;
        await _index.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
