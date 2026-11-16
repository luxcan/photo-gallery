using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.Sharing;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Sharing;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Sharing;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The Sync screen leaving a folder: stopping, choosing another, and taking this
/// computer's files out of the one left behind.
/// </summary>
/// <remarks>
/// Through the view model and the app's own registration of its use cases, so
/// the screen resolves exactly the graph it resolves in the window. The
/// question itself is the window's and is read as text by
/// <see cref="SharingSectionTests"/>.
/// </remarks>
public sealed class LeavingTheSyncFolderTests : IDisposable
{
    private readonly TwoLibraries _house = new TwoLibraries().Sharing();
    private ServiceProvider? _services;

    private Library Mum => _house.Mum;

    [Fact]
    public async Task ChoosingAnotherFolderLeavesTheOldOneAndItsFilesCanBeTaken()
    {
        await Mum.Publishing.HandleAsync();
        string answers = Path.Combine(
            _house.SharedFolder,
            SharedFolderExchange.AnswersFolder,
            SharedFolderExchange.NameOf(Mum.MachineId));
        Assert.True(File.Exists(answers));

        SharingViewModel sharing = Screen();
        await sharing.RefreshAsync();
        string before = sharing.Folder;

        string next = Path.Combine(Path.GetDirectoryName(_house.SharedFolder)!, "next");
        Directory.CreateDirectory(next);
        await sharing.ChooseFolderAsync(next);

        Assert.True(sharing.HasLeft(before));

        await sharing.RemoveOwnFilesAsync(before);

        Assert.False(File.Exists(answers));
        Assert.Contains("have been removed", sharing.Status, StringComparison.Ordinal);
        Assert.Contains("small copies", sharing.Status, StringComparison.Ordinal);
        Assert.True(sharing.IsIdle);
    }

    [Fact]
    public async Task StoppingLeavesTheFolderAndNothingIsTakenUntilAsked()
    {
        await Mum.Publishing.HandleAsync();
        string answers = Path.Combine(
            _house.SharedFolder,
            SharedFolderExchange.AnswersFolder,
            SharedFolderExchange.NameOf(Mum.MachineId));

        SharingViewModel sharing = Screen();
        await sharing.RefreshAsync();
        string before = sharing.Folder;

        await sharing.StopSyncingAsync();

        Assert.False(sharing.HasFolder);
        Assert.True(sharing.HasLeft(before));
        Assert.True(File.Exists(answers));
    }

    [Fact]
    public async Task ChoosingTheSameFolderAgainLeavesNothingBehind()
    {
        SharingViewModel sharing = Screen();
        await sharing.RefreshAsync();
        string before = sharing.Folder;

        await sharing.ChooseFolderAsync(_house.SharedFolder + Path.DirectorySeparatorChar);

        Assert.False(sharing.HasLeft(before));
        Assert.False(sharing.HasLeft(string.Empty));
    }

    [Fact]
    public async Task AFolderThatCannotBeEmptiedIsSaidAndNotThrown()
    {
        // The folder still in use is refused by the use case; the screen says so
        // in its own line rather than taking the window down.
        SharingViewModel sharing = Screen();
        await sharing.RefreshAsync();

        await sharing.RemoveOwnFilesAsync(sharing.Folder);

        Assert.StartsWith("This computer's files could not be removed", sharing.Status, StringComparison.Ordinal);
        Assert.True(sharing.IsIdle);
    }

    [Fact]
    public async Task AFolderThatCannotBeReachedIsNotReportedAsEmpty()
    {
        SharingViewModel sharing = Screen();
        await sharing.RefreshAsync();
        string next = Path.Combine(Path.GetDirectoryName(_house.SharedFolder)!, "next");
        Directory.CreateDirectory(next);
        await sharing.ChooseFolderAsync(next);

        string unreachable = Path.Combine(Path.GetDirectoryName(_house.SharedFolder)!, "switched-off");
        await sharing.RemoveOwnFilesAsync(unreachable);

        Assert.StartsWith("This computer's files could not be removed", sharing.Status, StringComparison.Ordinal);
        Assert.Contains("cannot be reached", sharing.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing", sharing.Status, StringComparison.Ordinal);
        Assert.True(sharing.IsIdle);
    }

    public void Dispose()
    {
        _services?.Dispose();
        _house.Dispose();
    }

    private SharingViewModel Screen()
    {
        _services = new ServiceCollection()
            .AddPhotoGalleryHandlers()
            .AddSingleton<ILibraryIndex>(Mum.Index)
            .AddSingleton<IWorkingFolder>(Mum.Working)
            .AddSingleton<IDecisionReader>(Mum.Decisions)
            .AddSingleton<IDecisionRepository>(Mum.Writing)
            .AddSingleton<IDecisionExchange>(Mum.Exchange)
            .AddSingleton<IRenditionPool>(Mum.Pool)
            .AddSingleton<IRenditionTurner>(Mum.Renditions)
            .AddSingleton<IFaceRepository>(new SqliteFaceRepository(Mum.Db))
            .AddSingleton<ISyncFolderCleanup>(new SharedFolderCleanup())
            .BuildServiceProvider();

        return new SharingViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileActivityLog(Mum.Working));
    }
}
