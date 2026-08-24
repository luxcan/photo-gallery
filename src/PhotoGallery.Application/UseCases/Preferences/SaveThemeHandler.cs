using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Preferences;

/// <summary>Remembers the palette the user chose, so it survives a restart.</summary>
public sealed class SaveThemeHandler
{
    private readonly ILibraryIndex _index;

    public SaveThemeHandler(ILibraryIndex index) => _index = index;

    public async Task HandleAsync(
        ThemePreference theme,
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings = await _index.GetSettingsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings.Theme == theme)
        {
            return;
        }

        settings.Theme = theme;
        await _index.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
