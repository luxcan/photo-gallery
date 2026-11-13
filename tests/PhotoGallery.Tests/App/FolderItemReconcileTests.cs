using System.Collections.ObjectModel;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The folder tree on screen brought into line with the tree as read again.
/// </summary>
/// <remarks>
/// The folders kept across a read are the objects the tree on screen holds its
/// expansion and its highlight against, so they must survive - but the order
/// on screen is the order as read, or a folder put in by a later read lands
/// wherever the leftovers of the last one happened to put it.
/// </remarks>
public sealed class FolderItemReconcileTests
{
    [Fact]
    public void KeptFoldersAreMovedIntoTheOrderAsRead()
    {
        ObservableCollection<FolderItem> shown = [Item("B"), Item("A")];
        FolderItem a = shown[1];
        FolderItem b = shown[0];

        FolderItem.Reconcile(shown, [Node("A"), Node("B")]);

        Assert.Equal(["A", "B"], shown.Select(folder => folder.Name));
        Assert.Same(a, shown[0]);
        Assert.Same(b, shown[1]);
    }

    [Fact]
    public void ANewFolderLandsWhereTheReadPutsIt()
    {
        // Out of order before the read, which is what a new folder used to be
        // placed against: inserted at its index in the read, into a list whose
        // other folders had not been put in that order, it landed between them.
        ObservableCollection<FolderItem> shown = [Item("C"), Item("A")];

        FolderItem.Reconcile(shown, [Node("A"), Node("B"), Node("C")]);

        Assert.Equal(["A", "B", "C"], shown.Select(folder => folder.Name));
    }

    [Fact]
    public void ChildrenAreOrderedAsReadToo()
    {
        ObservableCollection<FolderItem> shown =
        [
            new FolderItem(Node("2020", Node(@"2020\b"), Node(@"2020\a"))),
        ];
        FolderItem year = shown[0];

        FolderItem.Reconcile(shown, [Node("2020", Node(@"2020\a"), Node(@"2020\b"), Node(@"2020\c"))]);

        Assert.Same(year, shown[0]);
        Assert.Equal(["a", "b", "c"], year.Children.Select(folder => folder.Name));
    }

    [Fact]
    public void AFolderThatIsGoneIsTakenOut_AndTheRestKeepTheirObjects()
    {
        ObservableCollection<FolderItem> shown = [Item("A"), Item("B"), Item("C")];
        FolderItem c = shown[2];

        FolderItem.Reconcile(shown, [Node("A"), Node("C", count: 7)]);

        Assert.Equal(["A", "C"], shown.Select(folder => folder.Name));
        Assert.Same(c, shown[1]);
        Assert.Equal(7, c.ItemCount);
    }

    [Fact]
    public void Holds_FindsAFolderAtAnyDepth_AndNotOneTakenOut()
    {
        ObservableCollection<FolderItem> shown =
        [
            new FolderItem(Node("2020", Node(@"2020\a"), Node(@"2020\b"))),
        ];
        FolderItem b = shown[0].Children[1];

        Assert.True(FolderItem.Holds(shown, b));

        FolderItem.Reconcile(shown, [Node("2020", Node(@"2020\a"))]);

        Assert.False(FolderItem.Holds(shown, b));
    }

    private static FolderItem Item(string folder) => new(Node(folder));

    private static FolderNode Node(string folder, params FolderNode[] children) =>
        Node(folder, count: 1, children);

    private static FolderNode Node(string folder, int count, params FolderNode[] children) =>
        new(1, folder, folder[(folder.LastIndexOf('\\') + 1)..], count, children);
}
