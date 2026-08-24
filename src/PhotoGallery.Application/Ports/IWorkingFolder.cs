namespace PhotoGallery.Application.Ports;

/// <summary>
/// The folder holding everything this app creates: the index, the thumbnail
/// cache, the models and the quarantine.
/// </summary>
/// <remarks>
/// Keeping all derived data under one root is what makes a library portable -
/// copy the folder to another drive and it still opens.
/// </remarks>
public interface IWorkingFolder
{
    string Root { get; }

    string DatabasePath { get; }

    string ThumbnailsPath { get; }

    string ModelsPath { get; }

    string QuarantinePath { get; }

    string LogsPath { get; }

    /// <summary>Creates any missing subfolders. Safe to call repeatedly.</summary>
    void EnsureCreated();

    /// <summary>
    /// True when a path is one the app itself owns - the thumbnail cache,
    /// models, quarantine or logs.
    /// </summary>
    /// <remarks>
    /// The working folder may legitimately double as a photo source when the
    /// user points at a folder that already holds pictures, so scanning cannot
    /// simply skip the whole root; it must skip exactly these.
    /// </remarks>
    bool IsAppOwned(string path);
}
