using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.OpenLibrary;

/// <summary>Outcome of opening a working folder.</summary>
public sealed record OpenLibraryResult(
    string WorkingFolder,
    IReadOnlyList<PhotoSource> Sources,
    IReadOnlyDictionary<int, int> FileCountsBySource,
    LibraryCounts Counts,
    ThemePreference Theme,
    double GalleryCellSize,
    GallerySortOrder GallerySortOrder,
    bool NavigationCollapsed,
    bool WasCreated)
{
    /// <summary>Indexed files for one source, or zero when it has never been scanned.</summary>
    public int FileCountFor(int photoSourceId) =>
        FileCountsBySource.TryGetValue(photoSourceId, out int count) ? count : 0;

    /// <summary>
    /// True when no photos are connected yet, so the shell should lead with
    /// adding a source rather than with an empty browse view.
    /// </summary>
    public bool HasNoSources => Sources.Count == 0;
}
