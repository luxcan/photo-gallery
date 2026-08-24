namespace PhotoGallery.Application.UseCases.Scanning;

/// <summary>Progress of a running scan, reported as it goes.</summary>
/// <param name="Folder">
/// The folder the walk is inside at this moment, relative to the source, or
/// empty at the source's own root.
/// </param>
/// <remarks>
/// The folder is here because a crawl cannot say how far along it is - it does
/// not know how many files there are until it has found them - so the bar can
/// only report that something is happening. Naming the folder is what turns that
/// into progress somebody can read: on this library it walks 219 of them, and
/// seeing the name change is the difference between waiting and wondering
/// whether it has hung.
/// </remarks>
public readonly record struct ScanProgress(
    string SourcePath,
    int Seen,
    int Added,
    int Updated,
    int Unchanged,
    string Folder = "");
