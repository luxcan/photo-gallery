using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Storage;

/// <inheritdoc cref="IWorkingFolder"/>
public sealed class WorkingFolder : IWorkingFolder
{
    public WorkingFolder(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>
    /// The file that makes a folder a library.
    /// </summary>
    /// <remarks>
    /// Public because the app has to answer "is this a library?" before any
    /// container exists, and it was being answered with a copy of this string in
    /// two other files.
    /// </remarks>
    public const string DatabaseFileName = "index.db";

    public string Root { get; }

    public string DatabasePath => Path.Combine(Root, DatabaseFileName);

    public string ThumbnailsPath => Path.Combine(Root, "thumbs");

    public string ModelsPath => Path.Combine(Root, "models");

    public string QuarantinePath => Path.Combine(Root, "quarantine");

    public string LogsPath => Path.Combine(Root, "logs");

    /// <summary>Whether a folder already holds a library.</summary>
    public static bool IsLibrary(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        try
        {
            return File.Exists(Path.Combine(folder, DatabaseFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            // An unreachable drive or a malformed path is not a library.
            return false;
        }
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ThumbnailsPath);
        Directory.CreateDirectory(ModelsPath);
        Directory.CreateDirectory(QuarantinePath);
        Directory.CreateDirectory(LogsPath);
    }

    public bool IsAppOwned(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path).TrimEnd('\\', '/');

        foreach (string owned in new[] { ThumbnailsPath, ModelsPath, QuarantinePath, LogsPath })
        {
            if (string.Equals(full, owned, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(owned + Path.DirectorySeparatorChar,
                                   StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
