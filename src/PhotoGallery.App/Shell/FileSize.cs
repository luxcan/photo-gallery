namespace PhotoGallery.App.Shell;

/// <summary>A byte count as a person would say it.</summary>
/// <remarks>
/// Binary units, because this is what the file system reports and what Explorer
/// shows beside the same file - a KB that meant 1,000 here and 1,024 there would
/// make the two disagree over every picture.
/// </remarks>
public static class FileSize
{
    private const long Kilobyte = 1024L;
    private const long Megabyte = Kilobyte * 1024L;
    private const long Gigabyte = Megabyte * 1024L;

    /// <summary>The size in the largest unit that leaves a number worth reading.</summary>
    public static string Rounded(long bytes) => bytes switch
    {
        >= Gigabyte => $"{bytes / (double)Gigabyte:N2} GB",
        >= Megabyte => $"{bytes / (double)Megabyte:N1} MB",
        _ => $"{bytes / (double)Kilobyte:N0} KB",
    };
}
