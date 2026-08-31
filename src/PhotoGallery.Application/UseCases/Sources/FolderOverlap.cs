namespace PhotoGallery.Application.UseCases.Sources;

/// <summary>
/// Whether two folders are the same folder, or one holds the other.
/// </summary>
/// <remarks>
/// One rule in one place because it is asked in both directions and from two
/// features. A photo source may not overlap another source, and the shared
/// folder may not overlap a source either way round - and that second pair is
/// exactly the check that gets half built. Refusing a shared folder inside a
/// source while allowing a source to be added one level above the shared folder
/// leaves the hole the first half exists to close.
/// </remarks>
public static class FolderOverlap
{
    /// <summary>True when either folder is the other, or contains it.</summary>
    public static bool Any(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        string one = Normalise(left);
        string other = Normalise(right);

        return string.Equals(one, other, StringComparison.OrdinalIgnoreCase)
            || Holds(one, other)
            || Holds(other, one);
    }

    /// <summary>True when the parent contains the child, at any depth.</summary>
    public static bool Holds(string parent, string child)
    {
        string normalisedParent = Normalise(parent);
        string prefix = Path.EndsInDirectorySeparator(normalisedParent)
            ? normalisedParent
            : normalisedParent + Path.DirectorySeparatorChar;

        return Normalise(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one form both sides are compared in.
    /// </summary>
    /// <remarks>
    /// Comparing text is not enough on its own - a UNC path and a mapped drive
    /// letter are the same folder written two ways, and nothing here can tell -
    /// but it catches the mistake this is for, which is somebody nominating a
    /// folder they can see is inside another one.
    /// </remarks>
    public static string Normalise(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
}
