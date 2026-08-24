namespace PhotoGallery.Domain.Assets;

/// <summary>
/// Decides, from a file name alone, whether the library cares about a file.
/// </summary>
/// <remarks>
/// Extension-only on purpose: a scan walks tens of thousands of files, and
/// opening each one to sniff its contents would turn a fifteen-second pass into
/// an hours-long one. Anything misclassified here is caught later, when the file
/// is actually decoded.
/// </remarks>
public static class MediaFileTypes
{
    private static readonly HashSet<string> s_photo = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".heic", ".heif", ".webp", ".gif", ".bmp",
        ".tif", ".tiff", ".jfif", ".avif",
    };

    private static readonly HashSet<string> s_video = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".avi", ".mts", ".m2ts", ".mkv", ".wmv", ".3gp", ".mpg", ".mpeg",
    };

    /// <summary>
    /// Sidecars and thumbnails databases that sit beside photos but are not
    /// photos. Skipping them by name avoids thousands of pointless rows.
    /// </summary>
    private static readonly HashSet<string> s_ignoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbs.db", "desktop.ini", ".ds_store", "picasa.ini",
    };

    public static AssetKind Classify(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        if (s_ignoredNames.Contains(Path.GetFileName(fileName)))
        {
            return AssetKind.Unknown;
        }

        string extension = Path.GetExtension(fileName);
        if (extension.Length == 0)
        {
            return AssetKind.Unknown;
        }

        if (s_photo.Contains(extension))
        {
            return AssetKind.Photo;
        }

        return s_video.Contains(extension) ? AssetKind.Video : AssetKind.Unknown;
    }

    public static bool IsMedia(string fileName) => Classify(fileName) != AssetKind.Unknown;
}
