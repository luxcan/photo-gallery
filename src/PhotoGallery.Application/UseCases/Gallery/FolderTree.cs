using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Gallery;

/// <summary>
/// Turns a flat list of relative paths into the folder tree the gallery shows,
/// and works out the bounds that select one folder and everything under it.
/// </summary>
/// <remarks>
/// Pure: no I/O, no database. It is the part of the folder view most worth
/// testing, because the traps are all in string handling rather than in queries.
/// </remarks>
public static class FolderTree
{
    private const char Separator = '\\';

    /// <summary>The folder a file sits in, empty for a file at the source root.</summary>
    public static string FolderOf(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string normalised = Normalise(relativePath);
        int cut = normalised.LastIndexOf(Separator);
        return cut < 0 ? string.Empty : normalised[..cut];
    }

    /// <summary>
    /// The half-open range of relative paths that a folder and its descendants
    /// occupy, in ordinal order.
    /// </summary>
    /// <remarks>
    /// The upper bound is the separator's immediate successor - <c>]</c> is 0x5D
    /// and <c>\</c> is 0x5C - so nothing sorts between them and the range is
    /// exactly the subtree.
    ///
    /// <para>Appending the separator to the lower bound is what excludes a
    /// sibling that merely starts with the same characters. This library has
    /// eight such pairs, including <c>20220201</c> beside
    /// <c>20220201 - CNY</c>, so a prefix match would silently merge them.</para>
    /// </remarks>
    public static (string From, string Before) SubtreeBounds(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        string normalised = Normalise(folder).TrimEnd(Separator);
        return (normalised + Separator, normalised + ']');
    }

    /// <summary>
    /// Builds one tree per source, with each folder counting everything beneath
    /// it as well as in it.
    /// </summary>
    /// <param name="sourceNames">
    /// What to call each source's root node. A source is the top of its own
    /// tree, so the folders beneath it are visibly its own - with several
    /// sources, two folders of the same name would otherwise be
    /// indistinguishable.
    /// </param>
    public static IReadOnlyList<FolderNode> Build(
        IEnumerable<(int PhotoSourceId, string RelativePath)> files,
        IReadOnlyDictionary<int, string>? sourceNames = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        var direct = new Dictionary<(int Source, string Folder), int>();
        foreach ((int source, string relativePath) in files)
        {
            string folder = FolderOf(relativePath);
            if (folder.Length == 0)
            {
                continue;
            }

            direct.TryGetValue((source, folder), out int count);
            direct[(source, folder)] = count + 1;
        }

        // Ancestors that hold no files of their own still need a node, or a
        // nested folder would have nothing to hang from.
        var all = new HashSet<(int Source, string Folder)>(direct.Keys);
        foreach ((int source, string folder) in direct.Keys)
        {
            for (string? parent = ParentOf(folder); parent is not null; parent = ParentOf(parent))
            {
                all.Add((source, parent));
            }
        }

        var totals = new Dictionary<(int Source, string Folder), int>(all.Count);
        foreach ((int source, string folder) in all)
        {
            (string from, string before) = SubtreeBounds(folder);
            int total = 0;
            foreach (((int Source, string Folder) key, int count) in direct)
            {
                if (key.Source == source && Covers(key.Folder, folder, from, before))
                {
                    total += count;
                }
            }

            totals[(source, folder)] = total;
        }

        List<FolderNode> tops = [.. all
            .Where(node => ParentOf(node.Folder) is null)
            .Select(node => NodeFor(node.Source, node.Folder, all, totals))
            .OrderBy(node => node.PhotoSourceId)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)];

        if (sourceNames is null)
        {
            return tops;
        }

        // An empty RelativeFolder marks the source itself: selecting it means
        // everything in that source rather than any one folder.
        return [.. tops
            .GroupBy(node => node.PhotoSourceId)
            .Select(group => new FolderNode(
                group.Key,
                string.Empty,
                sourceNames.TryGetValue(group.Key, out string? name) ? name : "Photos",
                group.Sum(child => child.ItemCount),
                [.. group]))
            .OrderBy(node => node.PhotoSourceId)];
    }

    private static FolderNode NodeFor(
        int source,
        string folder,
        HashSet<(int Source, string Folder)> all,
        Dictionary<(int Source, string Folder), int> totals)
    {
        List<FolderNode> children = [.. all
            .Where(candidate => candidate.Source == source
                && string.Equals(ParentOf(candidate.Folder), folder, StringComparison.Ordinal))
            .Select(child => NodeFor(source, child.Folder, all, totals))
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)];

        return new FolderNode(source, folder, NameOf(folder), totals[(source, folder)], children);
    }

    /// <summary>Whether one folder is the given folder or sits beneath it.</summary>
    private static bool Covers(string candidate, string folder, string from, string before) =>
        string.Equals(candidate, folder, StringComparison.Ordinal)
        || (string.CompareOrdinal(candidate, from) >= 0
            && string.CompareOrdinal(candidate, before) < 0);

    private static string? ParentOf(string folder)
    {
        int cut = folder.LastIndexOf(Separator);
        return cut < 0 ? null : folder[..cut];
    }

    private static string NameOf(string folder)
    {
        int cut = folder.LastIndexOf(Separator);
        return cut < 0 ? folder : folder[(cut + 1)..];
    }

    private static string Normalise(string path) => path.Replace('/', Separator);
}
