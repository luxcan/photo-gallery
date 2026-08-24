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
            if (Overlaps(trimmed, other))
            {
                throw new InvalidOperationException(
                    $"That folder overlaps one already in the library: {source.Path}");
            }
        }

        return await _index.AddSourceAsync(trimmed, cancellationToken).ConfigureAwait(false);
    }

    private static string Normalise(string path) =>
        Path.GetFullPath(path.Trim()).TrimEnd('\\', '/');

    /// <summary>True when either path is the other, or contains it.</summary>
    private static bool Overlaps(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
        || Contains(left, right)
        || Contains(right, left);

    private static bool Contains(string parent, string child) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
