using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Sources;

/// <summary>
/// Adds a folder, drive or network share to the library's photo sources. The
/// app only ever reads from sources.
/// </summary>
public sealed class AddPhotoSourceHandler
{
    private readonly ILibraryIndex _index;
    private readonly IWorkingFolder _workingFolder;

    public AddPhotoSourceHandler(ILibraryIndex index, IWorkingFolder workingFolder)
    {
        _index = index;
        _workingFolder = workingFolder;
    }

    public async Task<PhotoSource> HandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string trimmed = Normalise(path);
        if (!Directory.Exists(trimmed))
        {
            throw new DirectoryNotFoundException($"The folder is not reachable: {trimmed}");
        }

        // The working folder itself is allowed - a user who points set-up at a
        // folder of pictures expects those pictures indexed. What must never be
        // a source is the app's own data, which scanning skips by the same rule.
        if (_workingFolder.IsAppOwned(trimmed))
        {
            throw new InvalidOperationException(
                "That folder belongs to Photo Gallery itself. "
              + "Choose a folder that holds your photos instead.");
        }

        // The other direction of the shared folder's own rule, and the easy half
        // to leave out. Refusing a shared folder inside a source while allowing a
        // source to be added one level above the shared folder permits exactly
        // the outcome the first half exists to prevent: sharing writes .jpg files
        // into a folder tree, and a scan would index them as photographs and grow
        // the library a second copy of itself on every Refresh.
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(settings.SharedFolder)
            && FolderOverlap.Any(trimmed, settings.SharedFolder))
        {
            throw new InvalidOperationException(
                $"That folder overlaps the one this library shares answers through: "
              + $"{settings.SharedFolder}. Scanning it would index Photo Gallery's own files "
              + "as photographs.");
        }

        IReadOnlyList<PhotoSource> existing = await _index.GetSourcesAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (PhotoSource source in existing)
        {
            string other = Normalise(source.Path);
            if (string.Equals(other, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Already part of the library: {trimmed}");
            }

            // Nesting would index the same files twice.
            if (FolderOverlap.Any(trimmed, other))
            {
                throw new InvalidOperationException(
                    $"That folder overlaps one already in the library: {source.Path}");
            }
        }

        return await _index.AddSourceAsync(trimmed, cancellationToken).ConfigureAwait(false);
    }

    private static string Normalise(string path) => FolderOverlap.Normalise(path);
}
