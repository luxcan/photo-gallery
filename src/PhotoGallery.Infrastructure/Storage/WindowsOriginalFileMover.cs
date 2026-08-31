using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Storage;

/// <inheritdoc cref="IOriginalFileMover"/>
public sealed class WindowsOriginalFileMover : IOriginalFileMover
{
    public bool DirectoryExists(string fullPath) => Directory.Exists(fullPath);

    public IReadOnlyList<string> GetFileNames(string fullPath) =>
        [.. Directory.EnumerateFiles(fullPath).Select(path => Path.GetFileName(path))];

    public bool HasDirectoryLink(string sourceRoot, string destinationFolder)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationFolder));

        string relative = Path.GetRelativePath(root, destination);
        if (relative == ".")
        {
            return IsLink(root);
        }

        string current = root;
        if (IsLink(current))
        {
            return true;
        }

        foreach (string segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsLink(current))
            {
                return true;
            }
        }

        return false;
    }

    public OriginalFileSnapshot? Inspect(string fullPath)
    {
        var file = new FileInfo(fullPath);
        return file.Exists
            ? new OriginalFileSnapshot(file.Length, file.LastWriteTimeUtc)
            : null;
    }

    public void Move(string sourceFullPath, string destinationFullPath)
    {
        string? folder = Path.GetDirectoryName(destinationFullPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException("The destination has no folder.");
        }

        Directory.CreateDirectory(folder);
        File.Move(sourceFullPath, destinationFullPath, overwrite: false);
    }

    private static bool IsLink(string folder)
    {
        var info = new DirectoryInfo(folder);
        return info.Exists
            && ((info.Attributes & FileAttributes.ReparsePoint) != 0
                || info.LinkTarget is not null);
    }
}
