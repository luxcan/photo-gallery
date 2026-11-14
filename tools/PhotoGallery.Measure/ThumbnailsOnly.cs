using PhotoGallery.Application.Ports;

namespace PhotoGallery.Measure;

/// <summary>
/// A working folder that knows only where the thumbnails are, so the app's own
/// <c>FileSystemThumbnailStore</c> can name each preview rather than the
/// harness repeating its sharding rule.
/// </summary>
internal sealed class ThumbnailsOnly : IWorkingFolder
{
    public ThumbnailsOnly(string thumbnailsPath) => ThumbnailsPath = thumbnailsPath;

    public string ThumbnailsPath { get; }

    public string Root => throw Unused();

    public string DatabasePath => throw Unused();

    public string ModelsPath => throw Unused();

    public string QuarantinePath => throw Unused();

    public string LogsPath => throw Unused();

    public void EnsureCreated() => throw Unused();

    public bool IsAppOwned(string path) => throw Unused();

    private static NotSupportedException Unused() =>
        new("The measurement harness only reads previews.");
}
