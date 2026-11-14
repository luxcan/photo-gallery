using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.Sharing;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Sharing;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What the Sync screen shows, and whether Sync now can be pressed, while the
/// folder is asked whether it is there - and once it has answered, or not.
/// </summary>
/// <remarks>
/// Found on the family's own library with the drive in the house switched off.
/// The screen said nothing for twenty-three seconds and then that the folder
/// could not be reached; and for all of those seconds Sync now could be
/// pressed, into a folder that was not there, to wait the same again.
///
/// <para>Through the view model and the app's own registration of its use
/// cases, because the button's state is the view model's and the deadline is
/// the use cases' - and a test of either alone would pass with the two of them
/// not joined up.</para>
/// </remarks>
public sealed class SyncScreenDeadlineTests : IDisposable
{
    /// <summary>How long the folder is given, where a test wants the deadline to pass.</summary>
    private static readonly TimeSpan s_deadline = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long the folder is given where a test wants it still being asked
    /// while it looks - long enough that a slow machine cannot turn the check
    /// into a refusal underneath the assertion.
    /// </summary>
    private static readonly TimeSpan s_patient = TimeSpan.FromMinutes(1);

    /// <summary>Far longer than the deadline, and far shorter than no deadline at all.</summary>
    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(10);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();
    private readonly SlowSyncFolder _folder;
    private ServiceProvider? _services;

    public SyncScreenDeadlineTests() => _folder = new SlowSyncFolder(Dad);

    private Library Dad => _house.Dad;

    [Fact]
    public async Task AFolderThatNeverAnswersIsSaidPlainlyInTime_AndSyncNowStaysOffUntilItDoes()
    {
        SharingViewModel sharing = Screen(s_deadline);

        await sharing.RefreshAsync().WaitAsync(s_bound);

        Assert.False(sharing.IsChecking);
        Assert.StartsWith("That folder did not respond within ", sharing.Problem, StringComparison.Ordinal);
        Assert.EndsWith(_house.SharedFolder, sharing.Problem, StringComparison.Ordinal);
        Assert.False(sharing.ShareCommand.CanExecute(null));
        Assert.False(sharing.FolderWasThere);

        // The next look, once the drive is back, is what turns it on again.
        _folder.Answers();
        await sharing.RefreshAsync().WaitAsync(s_bound);

        Assert.Empty(sharing.Problem);
        Assert.True(sharing.ShareCommand.CanExecute(null));
        Assert.True(sharing.FolderWasThere);
    }

    [Fact]
    public async Task SyncNowIsOffWhileTheFolderIsBeingChecked_AndOnOnceItAnswers()
    {
        SharingViewModel sharing = Screen(s_patient);

        _folder.Answers();
        await sharing.RefreshAsync().WaitAsync(s_bound);
        Assert.True(sharing.ShareCommand.CanExecute(null));

        // The drive goes to sleep while the app stays open, and the screen is
        // opened again: the folder is known and nothing is wrong yet, which is
        // exactly when the button used to be live.
        _folder.GoesQuiet();
        Task opening = sharing.RefreshAsync();
        await _folder.Asked.WaitAsync(s_bound);

        Assert.False(
            sharing.ShareCommand.CanExecute(null),
            "Sync now can be pressed while the folder is still being asked");
        Assert.True(sharing.IsChecking, "the screen does not say it is checking the folder");
        Assert.Equal(_house.SharedFolder, sharing.Folder);

        _folder.Answers();
        await opening.WaitAsync(s_bound);

        Assert.False(sharing.IsChecking);
        Assert.Empty(sharing.Problem);
        Assert.True(sharing.ShareCommand.CanExecute(null));
    }

    [Fact]
    public async Task SyncNowAsksTheFolderAgainAndStopsAtTheDeadlineWhenItHasGone()
    {
        SharingViewModel sharing = Screen(s_deadline);

        _folder.Answers();
        await sharing.RefreshAsync().WaitAsync(s_bound);
        Assert.True(sharing.ShareCommand.CanExecute(null));

        // Gone between opening the screen and pressing the button.
        _folder.GoesQuiet();
        await sharing.ShareCommand.ExecuteAsync(null).WaitAsync(s_bound);

        Assert.StartsWith("That folder did not respond within ", sharing.Problem, StringComparison.Ordinal);
        Assert.False(sharing.IsBusy);
        Assert.False(sharing.ShareCommand.CanExecute(null));
        Assert.False(
            Directory.Exists(Path.Combine(_house.SharedFolder, "answers")),
            "a sync into a folder that did not answer wrote to it");
    }

    /// <summary>
    /// A press of Sync now that finds the folder gone still leaves its two lines
    /// in the activity log.
    /// </summary>
    /// <remarks>
    /// The red line on the screen lasts until the next look and no longer, and
    /// the log is where a share leaves a trace for after that. A press stopped by
    /// the folder used to say there which folder it tried and why it could not;
    /// stopped before the share began, it said nothing at all.
    /// </remarks>
    [Fact]
    public async Task ASyncNowThatFindsTheFolderGoneSaysSoInTheActivityLog()
    {
        SharingViewModel sharing = Screen(s_deadline);

        _folder.Answers();
        await sharing.RefreshAsync().WaitAsync(s_bound);

        _folder.GoesQuiet();
        await sharing.ShareCommand.ExecuteAsync(null).WaitAsync(s_bound);

        string log = ActivityLog();
        Assert.Contains($"syncing through {_house.SharedFolder}", log, StringComparison.Ordinal);
        Assert.Contains(
            $"  {SyncFolderAnswer.Silent(_house.SharedFolder, s_deadline).Problem}",
            log,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// "Change..." asks the folder again before the picker is handed it, and a
    /// folder gone since the screen last looked is not handed over.
    /// </summary>
    /// <remarks>
    /// The screen can stay open for hours while the drive is switched off or the
    /// laptop sleeps and wakes out of the house. Going by what the last look
    /// found, the picker was given an address nothing answered: the window froze
    /// while the shell gave up on it, and then the picker refused to open, or
    /// threw.
    /// </remarks>
    [Fact]
    public async Task ThePickerOpensNowhereInParticularWhenTheFolderHasGoneSinceTheLastLook()
    {
        SharingViewModel sharing = Screen(s_deadline);

        _folder.Answers();
        await sharing.RefreshAsync().WaitAsync(s_bound);
        Assert.True(sharing.FolderWasThere);

        _folder.GoesQuiet();
        string start = await sharing.FolderToBrowseFromAsync().WaitAsync(s_bound);

        Assert.Empty(start);
        Assert.StartsWith("That folder did not respond within ", sharing.Problem, StringComparison.Ordinal);
        Assert.False(sharing.ShareCommand.CanExecute(null));
        Assert.True(sharing.IsIdle);

        // Known gone now, so a second press opens the picker at once rather than
        // waiting out the deadline again for somebody choosing another folder.
        Task<string> again = sharing.FolderToBrowseFromAsync();
        Assert.True(again.IsCompleted, "the picker waited on a folder already known to be gone");
        Assert.Empty(await again);
    }

    /// <summary>
    /// While the picker's question is out, "Change..." and Sync now are off and
    /// the screen says it is checking; a folder that answers is where the picker
    /// opens.
    /// </summary>
    /// <remarks>
    /// Off, so that a second press cannot put a second picker over the first
    /// when the folder answers.
    /// </remarks>
    [Fact]
    public async Task ChangeIsOffWhileThePickerAsksTheFolder_AndItOpensThereOnceTheFolderAnswers()
    {
        SharingViewModel sharing = Screen(s_patient);

        _folder.Answers();
        await sharing.RefreshAsync().WaitAsync(s_bound);

        _folder.GoesQuiet();
        Task<string> opening = sharing.FolderToBrowseFromAsync();
        await _folder.Asked.WaitAsync(s_bound);

        Assert.False(sharing.IsIdle, "Change... can be pressed while the folder is asked again");
        Assert.True(sharing.IsChecking, "the screen does not say it is checking the folder");
        Assert.False(sharing.ShareCommand.CanExecute(null));

        _folder.Answers();

        Assert.Equal(_house.SharedFolder, await opening.WaitAsync(s_bound));
        Assert.True(sharing.IsIdle);
        Assert.True(sharing.ShareCommand.CanExecute(null));
    }

    /// <summary>Everything Dad's library has written to its activity log.</summary>
    private string ActivityLog() =>
        Directory.Exists(Dad.Working.LogsPath)
            ? string.Concat(Directory
                .GetFiles(Dad.Working.LogsPath, "activity-*.log")
                .Select(File.ReadAllText))
            : string.Empty;

    /// <summary>
    /// The Sync screen over Dad's library, with its folder held by the test and
    /// given <paramref name="patience"/> to answer.
    /// </summary>
    /// <remarks>
    /// The app's own registration of its use cases, so the screen resolves
    /// exactly the graph it resolves in the window; only the library and its
    /// folder are this test's, and the deadline, so that nobody waits out the
    /// real one.
    /// </remarks>
    private SharingViewModel Screen(TimeSpan patience)
    {
        _services = new ServiceCollection()
            .AddPhotoGalleryHandlers()
            .AddSingleton<ILibraryIndex>(Dad.Index)
            .AddSingleton<IDecisionReader>(Dad.Decisions)
            .AddSingleton<IDecisionRepository>(Dad.Writing)
            .AddSingleton<IDecisionExchange>(_folder)
            .AddSingleton<IRenditionPool>(_folder)
            .AddSingleton<IRenditionTurner>(Dad.Renditions)
            .AddSingleton<IFaceRepository>(new SqliteFaceRepository(Dad.Db))
            .AddScoped(services => new SyncFolderProbe(
                services.GetRequiredService<ILibraryIndex>(),
                services.GetRequiredService<IRenditionPool>())
            {
                Patience = patience,
            })
            .BuildServiceProvider();

        return new SharingViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileActivityLog(Dad.Working));
    }

    public void Dispose()
    {
        _services?.Dispose();
        _house.Dispose();
    }
}
