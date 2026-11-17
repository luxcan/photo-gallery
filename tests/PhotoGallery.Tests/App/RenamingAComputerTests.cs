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
/// The Sync screen's pencil: asking what to call another computer, and asking
/// again where the name is one another computer already goes by.
/// </summary>
/// <remarks>
/// Through the view model and the app's own registration of its use cases, so
/// the screen resolves exactly the graph it resolves in the window. The dialog
/// is the window's; here it is a script of answers.
/// </remarks>
public sealed class RenamingAComputerTests : IDisposable
{
    private readonly TwoLibraries _house = new TwoLibraries().Sharing();
    private ServiceProvider? _services;

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task ANameTyped_IsWhatTheRowSays()
    {
        SharingViewModel sharing = await ScreenWithDadOnItAsync();
        var question = new ScriptedQuestion("Study desktop");
        sharing.NameQuestion = question;

        await sharing.RenameMachineCommand.ExecuteAsync(DadsRow(sharing));

        Assert.Equal("Study desktop", DadsRow(sharing).Name);
        (string current, string ownName, string? problem) = Assert.Single(question.Asked);
        Assert.Equal("Dad's laptop", current);
        Assert.Equal("Dad's laptop", ownName);
        Assert.Null(problem);
        Assert.True(sharing.IsIdle);
    }

    [Fact]
    public async Task ANameAlreadyTaken_IsAskedAgainWithTheReasonAndWhatWasTyped()
    {
        SharingViewModel sharing = await ScreenWithDadOnItAsync();
        _house.Add("Gran's laptop");
        var question = new ScriptedQuestion(" gran's laptop ", "Study desktop");
        sharing.NameQuestion = question;

        await sharing.RenameMachineCommand.ExecuteAsync(DadsRow(sharing));

        Assert.Equal(2, question.Asked.Count);
        Assert.Equal(" gran's laptop ", question.Asked[1].Current);
        Assert.Equal("Another computer is already called gran's laptop.", question.Asked[1].Problem);
        Assert.Equal("Study desktop", DadsRow(sharing).Name);
    }

    [Fact]
    public async Task Cancelling_KeepsTheNameItHad()
    {
        SharingViewModel sharing = await ScreenWithDadOnItAsync();
        sharing.NameQuestion = new ScriptedQuestion();

        await sharing.RenameMachineCommand.ExecuteAsync(DadsRow(sharing));

        Assert.Equal("Dad's laptop", DadsRow(sharing).Name);
        Assert.True(sharing.IsIdle);
    }

    [Fact]
    public async Task EveryComputerWrittenDown_OffersTheRename()
    {
        SharingViewModel sharing = await ScreenWithDadOnItAsync();

        MachineRow dad = DadsRow(sharing);
        Assert.True(dad.CanBeNamed);
        Assert.Equal("Dad's laptop", dad.OwnName);
    }

    /// <summary>Mum's Sync screen, after Dad has shared and Mum has synced.</summary>
    private async Task<SharingViewModel> ScreenWithDadOnItAsync()
    {
        Dad.Person("Ana");
        await Dad.Publishing.HandleAsync();
        await Mum.Sharing.HandleAsync();

        SharingViewModel sharing = Screen();
        await sharing.RefreshAsync();
        return sharing;
    }

    private MachineRow DadsRow(SharingViewModel sharing) =>
        Assert.Single(sharing.Machines, row => row.MachineId == Dad.MachineId);

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

    public void Dispose()
    {
        _services?.Dispose();
        _house.Dispose();
    }

    /// <summary>Answers each question with the next name, then cancels.</summary>
    private sealed class ScriptedQuestion(params string[] answers) : IMachineNameQuestion
    {
        private readonly Queue<string> _answers = new(answers);

        public List<(string Current, string OwnName, string? Problem)> Asked { get; } = [];

        public string? AskName(string current, string ownName, string? problem)
        {
            Asked.Add((current, ownName, problem));
            return _answers.TryDequeue(out string? answer) ? answer : null;
        }
    }
}
