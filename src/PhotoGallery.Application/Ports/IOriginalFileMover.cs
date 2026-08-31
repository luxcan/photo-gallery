namespace PhotoGallery.Application.Ports;

/// <summary>The small file-system surface used by the recoverable album move.</summary>
public interface IOriginalFileMover
{
    bool DirectoryExists(string fullPath);

    IReadOnlyList<string> GetFileNames(string fullPath);

    /// <summary>
    /// Whether the route from <paramref name="sourceRoot"/> to
    /// <paramref name="destinationFolder"/> crosses a directory link or junction.
    /// </summary>
    bool HasDirectoryLink(string sourceRoot, string destinationFolder);

    OriginalFileSnapshot? Inspect(string fullPath);

    /// <summary>Moves without replacing an existing destination.</summary>
    void Move(string sourceFullPath, string destinationFullPath);
}

public sealed record OriginalFileSnapshot(long Length, DateTime ModifiedUtc);
