using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Gallery;

/// <summary>One folder in the tree beside the wall, as it is drawn.</summary>
/// <remarks>
/// A folder as read is a record and cannot change, so the tree used to be
/// replaced whole whenever it was read again. The tree on screen keeps which
/// folders are open and which one is chosen against the objects it was given,
/// so every re-read folded it back to its roots and lost the highlight, while
/// the wall stayed on the folder that had been chosen. Deleting a photograph
/// reads the tree again, so that happened after every delete. This is the
/// folder kept across reads: its count moves in place and the tree is left as
/// the reader arranged it.
/// </remarks>
public sealed partial class FolderItem : ObservableObject
{
    /// <summary>
    /// How many photographs the folder holds, counting every folder beneath it,
    /// as last read.
    /// </summary>
    [ObservableProperty]
    private int _itemCount;

    public FolderItem(FolderNode folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        PhotoSourceId = folder.PhotoSourceId;
        RelativeFolder = folder.RelativeFolder;
        Name = folder.Name;
        ItemCount = folder.ItemCount;
        Children = [.. folder.Children.Select(child => new FolderItem(child))];
    }

    public int PhotoSourceId { get; }

    public string RelativeFolder { get; }

    public string Name { get; }

    public ObservableCollection<FolderItem> Children { get; }

    /// <summary>
    /// Brings the folders on screen into line with the tree as read now,
    /// keeping every folder that is still there as the object it was.
    /// </summary>
    /// <remarks>
    /// A folder is the same folder when its source and its path are, whatever
    /// its count. One that is gone is taken out, one that is new is put in, and
    /// every one ends up exactly where the tree as read has it. A kept folder is
    /// moved rather than replaced, which keeps the object - and with it whether
    /// it is open and whether it is chosen.
    /// </remarks>
    /// <param name="shown">The folders on screen, changed in place.</param>
    /// <param name="read">The folders as read now, in the order to show them.</param>
    public static void Reconcile(ObservableCollection<FolderItem> shown, IReadOnlyList<FolderNode> read)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(read);

        for (int at = shown.Count - 1; at >= 0; at--)
        {
            if (!read.Any(folder => shown[at].IsFor(folder)))
            {
                shown.RemoveAt(at);
            }
        }

        // Everything before 'at' is already the read's own first folders in the
        // read's own order, so 'at' is never past the end of what is shown.
        for (int at = 0; at < read.Count; at++)
        {
            FolderNode folder = read[at];
            FolderItem? kept = shown.FirstOrDefault(item => item.IsFor(folder));

            if (kept is null)
            {
                shown.Insert(at, new FolderItem(folder));
                continue;
            }

            int from = shown.IndexOf(kept);
            if (from != at)
            {
                shown.Move(from, at);
            }

            kept.ItemCount = folder.ItemCount;
            Reconcile(kept.Children, folder.Children);
        }
    }

    /// <summary>
    /// Whether this very folder is anywhere in a tree, at any depth.
    /// </summary>
    /// <remarks>
    /// By object rather than by path, because <see cref="Reconcile"/> keeps the
    /// object of every folder that is still there - so a folder not found here
    /// is one a read took out.
    /// </remarks>
    public static bool Holds(IEnumerable<FolderItem> tree, FolderItem folder)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(folder);

        return tree.Any(item => ReferenceEquals(item, folder) || Holds(item.Children, folder));
    }

    private bool IsFor(FolderNode folder) =>
        folder.PhotoSourceId == PhotoSourceId
        && string.Equals(folder.RelativeFolder, RelativeFolder, StringComparison.Ordinal);
}
