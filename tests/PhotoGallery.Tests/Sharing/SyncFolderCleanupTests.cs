using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Taking this computer's files out of a folder it has stopped syncing through,
/// and nobody else's.
/// </summary>
/// <remarks>
/// What a computer leaves in the folder - names, birth years, faces, where and
/// when every photograph was taken - is readable by anybody who can open it, and
/// choosing another folder used to leave it there for good. The other half of
/// the rule matters as much: the folder is shared, so a removal that reached one
/// file belonging to another computer would be taking that computer's work.
/// </remarks>
public sealed class SyncFolderCleanupTests : IDisposable
{
    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    private string Old => _house.SharedFolder;

    [Fact]
    public async Task LeavingAFolderTakesThisComputersFilesAndNobodyElses()
    {
        string[] mine =
        [
            Plant(SharedFolderExchange.AnswersFolder, SharedFolderExchange.NameOf(Mum.MachineId)),
            Plant(SharedFolderExchange.AnswersFolder, SharedFolderExchange.NameOf(Mum.MachineId) + ".tmp"),
            Plant(SharedFolderPool.FactsFolder, SharedFolderPool.FactsNameOf(Mum.MachineId)),
            Plant(SharedFolderPool.VectorsFolder, SharedFolderPool.VectorsNameOf(Mum.MachineId)),
        ];

        string[] theirs =
        [
            Plant(SharedFolderExchange.AnswersFolder, SharedFolderExchange.NameOf(Dad.MachineId)),
            Plant(SharedFolderPool.FactsFolder, SharedFolderPool.FactsNameOf(Dad.MachineId)),
            Plant(SharedFolderPool.VectorsFolder, SharedFolderPool.VectorsNameOf(Dad.MachineId)),

            // Named to look like this computer's without being one of its files:
            // nothing is matched by pattern, so these are not touched either.
            Plant(SharedFolderExchange.AnswersFolder, SharedFolderExchange.NameOf(Mum.MachineId) + ".bak"),
            Plant(SharedFolderExchange.AnswersFolder, $"{Mum.MachineId:D}-copy.json.gz"),

            // The small copies are named after the photograph, so another
            // computer may have left the very same one. They stay.
            Plant(Path.Combine(SharedFolderPool.ThumbsFolder, "ab"), "abcdef.jpg"),
            Plant(Path.Combine(SharedFolderPool.ThumbsFolder, "ab"), "abcdef-p.jpg"),
        ];

        Mum.SharesThrough(Folder("elsewhere"));

        int removed = await Removing(Mum).HandleAsync(Old);

        Assert.Equal(mine.Length, removed);
        Assert.All(mine, file => Assert.False(File.Exists(file), $"{file} is still there"));
        Assert.All(theirs, file => Assert.True(File.Exists(file), $"{file} was taken"));

        // And the other computers see it as having gone.
        Assert.DoesNotContain(
            await Dad.Exchange.StandingAsync(), standing => standing.MachineId == Mum.MachineId);
    }

    [Fact]
    public async Task AFolderWithNothingOfThisComputersInItIsLeftAlone()
    {
        string theirs = Plant(
            SharedFolderExchange.AnswersFolder, SharedFolderExchange.NameOf(Dad.MachineId));
        Mum.SharesThrough(Folder("elsewhere"));

        Assert.Equal(0, await Removing(Mum).HandleAsync(Old));
        Assert.True(File.Exists(theirs));
    }

    [Fact]
    public async Task AFolderThatCannotBeReachedIsAFailureNotAnEmptyFolder()
    {
        // A drive that is switched off answers "no such file" for every one of
        // them. Counting that as nothing left would tell the person their files
        // are gone while every one of them is still there.
        Mum.SharesThrough(Folder("elsewhere"));
        string unreachable = Path.Combine(Path.GetDirectoryName(Old)!, "switched-off");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => Removing(Mum).HandleAsync(unreachable));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\\")]
    public async Task TheFolderStillInUseIsRefusedHoweverItIsWritten(string trailing)
    {
        // The next sync would only write the same files again, and in between
        // the other computers would see this one as having left.
        string mine = Plant(
            SharedFolderExchange.AnswersFolder, SharedFolderExchange.NameOf(Mum.MachineId));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Removing(Mum).HandleAsync(Old + trailing));

        Assert.True(File.Exists(mine));
    }

    [Fact]
    public async Task StoppingSyncingLeavesTheFolderFreeToBeEmptied()
    {
        string mine = Plant(
            SharedFolderExchange.AnswersFolder, SharedFolderExchange.NameOf(Mum.MachineId));
        await new SetSharedFolderHandler(Mum.Index, Mum.Working).ClearAsync();

        Assert.Equal(1, await Removing(Mum).HandleAsync(Old));
        Assert.False(File.Exists(mine));
    }

    public void Dispose() => _house.Dispose();

    private static RemoveOwnSyncFilesHandler Removing(Library library) =>
        new(library.Index, new SharedFolderCleanup());

    private string Folder(string name)
    {
        string folder = Path.Combine(Path.GetDirectoryName(Old)!, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string Plant(string under, string name)
    {
        string folder = Path.Combine(Old, under);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, name);
        File.WriteAllText(path, "planted");
        return path;
    }
}
