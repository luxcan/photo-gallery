using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;

namespace PhotoGallery.Tests.Application;

public sealed class FolderTreeTests
{
    [Fact]
    public void Build_MirrorsTheRealStructure()
    {
        IReadOnlyList<FolderNode> tree = FolderTree.Build([
            (1, @"A\x.jpg"),
            (1, @"A\sub\y.jpg"),
            (1, @"B\z.jpg"),
        ]);

        Assert.Equal(["A", "B"], tree.Select(n => n.Name));
        Assert.Equal(["sub"], tree[0].Children.Select(n => n.Name));
        Assert.Empty(tree[1].Children);
    }

    [Fact]
    public void Build_CountsEverythingBeneathAFolder()
    {
        // Selecting a folder shows its subfolders too, so the number beside it
        // has to mean the same thing.
        IReadOnlyList<FolderNode> tree = FolderTree.Build([
            (1, @"A\x.jpg"),
            (1, @"A\sub\y.jpg"),
            (1, @"A\sub\z.jpg"),
        ]);

        Assert.Equal(3, tree[0].ItemCount);
        Assert.Equal(2, tree[0].Children[0].ItemCount);
    }

    [Fact]
    public void Build_KeepsAPrefixSiblingOutOfItsNeighboursCount()
    {
        // Eight pairs of real folders collide this way. "20220201 - CNY" starts
        // with "20220201" but is not inside it.
        IReadOnlyList<FolderNode> tree = FolderTree.Build([
            (1, @"20220201\a.jpg"),
            (1, @"20220201 - CNY\b.jpg"),
            (1, @"20220201 - CNY\c.jpg"),
        ]);

        FolderNode bare = tree.Single(n => n.Name == "20220201");
        FolderNode named = tree.Single(n => n.Name == "20220201 - CNY");

        Assert.Equal(1, bare.ItemCount);
        Assert.Equal(2, named.ItemCount);
        Assert.Empty(bare.Children);
    }

    [Fact]
    public void Build_AttachesAChildToItsParentEvenWhenASiblingSortsBetweenThem()
    {
        // Ordinal order puts "20220201 - CNY" between "20220201" and
        // "20220201\sub", because a space is below a backslash. An approach that
        // assumed a parent is immediately followed by its children would hang
        // the child off the wrong folder.
        IReadOnlyList<FolderNode> tree = FolderTree.Build([
            (1, @"20220201\a.jpg"),
            (1, @"20220201 - CNY\b.jpg"),
            (1, @"20220201\sub\c.jpg"),
        ]);

        FolderNode bare = tree.Single(n => n.Name == "20220201");

        Assert.Equal(["sub"], bare.Children.Select(n => n.Name));
        Assert.Equal(2, bare.ItemCount);
        Assert.Empty(tree.Single(n => n.Name == "20220201 - CNY").Children);
    }

    [Fact]
    public void Build_KeepsSourcesApart()
    {
        IReadOnlyList<FolderNode> tree = FolderTree.Build([
            (1, @"shared\mine.jpg"),
            (2, @"shared\theirs.jpg"),
        ]);

        Assert.Equal(2, tree.Count);
        Assert.Equal([1, 2], tree.Select(n => n.PhotoSourceId));
        Assert.All(tree, n => Assert.Equal(1, n.ItemCount));
    }

    [Fact]
    public void Build_GivesAnAncestorANodeEvenWhenItHoldsNoFilesItself()
    {
        IReadOnlyList<FolderNode> tree = FolderTree.Build([(1, @"trips\2016\coast\a.jpg")]);

        Assert.Equal("trips", tree.Single().Name);
        Assert.Equal("2016", tree.Single().Children.Single().Name);
        Assert.Equal("coast", tree.Single().Children.Single().Children.Single().Name);
        Assert.Equal(1, tree.Single().ItemCount);
    }

    [Fact]
    public void Build_IgnoresFilesSittingAtASourceRoot()
    {
        // They belong to no folder, and inventing one for them would put a node
        // in the tree that does not exist on disk.
        IReadOnlyList<FolderNode> tree = FolderTree.Build([
            (1, "loose.jpg"),
            (1, @"A\x.jpg"),
        ]);

        Assert.Equal(["A"], tree.Select(n => n.Name));
    }

    [Fact]
    public void Build_AcceptsForwardSlashes()
    {
        IReadOnlyList<FolderNode> tree = FolderTree.Build([(1, "A/sub/x.jpg")]);

        Assert.Equal("A", tree.Single().Name);
        Assert.Equal("sub", tree.Single().Children.Single().Name);
    }

    [Fact]
    public void Build_OnAnEmptyLibraryReturnsNothing() =>
        Assert.Empty(FolderTree.Build([]));

    [Fact]
    public void Build_PutsTheSourceAtTheRootWhenItIsNamed()
    {
        // Without a root the tree starts at top-level folders, and with more than
        // one source two folders of the same name are indistinguishable.
        IReadOnlyList<FolderNode> tree = FolderTree.Build(
            [(1, @"A\x.jpg"), (1, @"B\y.jpg"), (2, @"A\z.jpg")],
            new Dictionary<int, string> { [1] = @"\\nas\photos", [2] = @"D:\Pictures" });

        Assert.Equal([@"\\nas\photos", @"D:\Pictures"], tree.Select(n => n.Name));
        Assert.Equal(2, tree[0].ItemCount);
        Assert.Equal(["A", "B"], tree[0].Children.Select(n => n.Name));
        Assert.Equal(1, tree[1].ItemCount);
    }

    [Fact]
    public void Build_LeavesTheSourceRootWithNoFolderOfItsOwn()
    {
        // An empty RelativeFolder is what tells the gallery "the whole source",
        // rather than a folder that happens to be named nothing.
        IReadOnlyList<FolderNode> tree = FolderTree.Build(
            [(1, @"A\x.jpg")],
            new Dictionary<int, string> { [1] = @"\\nas\photos" });

        Assert.Equal(string.Empty, tree.Single().RelativeFolder);
        Assert.Equal("A", tree.Single().Children.Single().RelativeFolder);
    }

    [Fact]
    public void Build_NamesASourceItDoesNotRecognise()
    {
        IReadOnlyList<FolderNode> tree = FolderTree.Build(
            [(9, @"A\x.jpg")], new Dictionary<int, string>());

        Assert.Equal("Photos", tree.Single().Name);
    }

    [Theory]
    [InlineData(@"A\b\c.jpg", @"A\b")]
    [InlineData(@"A\c.jpg", "A")]
    [InlineData("c.jpg", "")]
    public void FolderOf_ReturnsTheContainingFolder(string relativePath, string expected) =>
        Assert.Equal(expected, FolderTree.FolderOf(relativePath));

    [Fact]
    public void SubtreeBounds_SpanExactlyTheFolderAndItsDescendants()
    {
        (string from, string before) = FolderTree.SubtreeBounds("20220201");

        // The separator's immediate successor closes the range, so nothing sorts
        // between the two bounds that is not inside the folder.
        Assert.Equal(@"20220201\", from);
        Assert.Equal("20220201]", before);
        Assert.True(string.CompareOrdinal(@"20220201\a.jpg", from) >= 0);
        Assert.True(string.CompareOrdinal(@"20220201\a.jpg", before) < 0);
        Assert.False(string.CompareOrdinal("20220201 - CNY\\b.jpg", from) >= 0);
    }

    [Fact]
    public void SubtreeBounds_IgnoreATrailingSeparator() =>
        Assert.Equal(
            FolderTree.SubtreeBounds("A"),
            FolderTree.SubtreeBounds(@"A\"));
}
