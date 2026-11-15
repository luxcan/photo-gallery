using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Updates;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// That a new version is offered even when another notice holds the corner.
/// </summary>
/// <remarks>
/// Found testing the first real release: on opening, the sync folder is looked
/// at before GitHub, its "do you want to sync?" was already up, and the offer
/// to update was dropped - and counted as made, so nothing offered it again
/// until the app was restarted. It now waits for the corner.
/// </remarks>
public sealed class UpdateOfferTests : IDisposable
{
    private static readonly PublishedRelease Release = new(
        new Version(99, 0, 0),
        "Photo Gallery 99.0.0",
        "https://example/release",
        new ReleaseDownload(new Uri("https://example/PhotoGallery.exe"), 1, "abc", null));

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pg-update-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services;
    private readonly MainViewModel _shell;

    public UpdateOfferTests()
    {
        var working = new WorkingFolder(_root);
        working.EnsureCreated();
        var store = new FileSystemThumbnailStore(working);

        _services = new ServiceCollection()
            .AddSingleton<IReleaseSource>(new OneRelease(Release))
            .AddSingleton<IAppInstaller>(new ReplaceableInstaller())
            .AddTransient<CheckForNewVersionHandler>()
            .AddTransient<UpdateAppHandler>()
            .BuildServiceProvider();

        IServiceScopeFactory scopes = _services.GetRequiredService<IServiceScopeFactory>();
        _shell = new MainViewModel(
            scopes,
            new GalleryViewModel(scopes, store),
            new PeopleViewModel(scopes, store),
            new DuplicatesViewModel(scopes, store),
            store,
            new SilentLog());
    }

    [Fact]
    public async Task WithTheCornerFree_TheReleaseIsOfferedAtOnce()
    {
        await _shell.LookForANewVersionAsync();

        Assert.StartsWith("Photo Gallery 99.0.0 is available", _shell.Notice, StringComparison.Ordinal);
        Assert.Equal("Update", _shell.NoticeAction);
    }

    [Fact]
    public async Task ARelease_FoundUnderAnotherNotice_IsOfferedOnceThatNoticeIsPutDown()
    {
        _shell.Ask("Steph has made some changes. Do you want to sync?", "Sync", () => Task.CompletedTask);

        await _shell.LookForANewVersionAsync();
        Assert.StartsWith("Steph has made some changes", _shell.Notice, StringComparison.Ordinal);

        _shell.DismissNoticeCommand.Execute(null);

        Assert.StartsWith("Photo Gallery 99.0.0 is available", _shell.Notice, StringComparison.Ordinal);
        Assert.Equal("Update", _shell.NoticeAction);
    }

    [Fact]
    public async Task ARelease_FoundUnderAQuestion_WaitsForWhatTheAnswerSays()
    {
        // Answering a sync ends by saying how it went in the same corner; the
        // offer must not be put up only to be replaced by that report.
        _shell.Ask(
            "Steph has made some changes. Do you want to sync?",
            "Sync",
            () =>
            {
                _shell.Say("8 names changed. You're up to date.");
                return Task.CompletedTask;
            });

        await _shell.LookForANewVersionAsync();
        await _shell.AnswerNoticeCommand.ExecuteAsync(null);

        Assert.Equal("8 names changed. You're up to date.", _shell.Notice);

        _shell.DismissNoticeCommand.Execute(null);

        Assert.StartsWith("Photo Gallery 99.0.0 is available", _shell.Notice, StringComparison.Ordinal);
        Assert.Equal("Update", _shell.NoticeAction);
    }

    [Fact]
    public async Task AReleaseAlreadyOffered_IsNotOfferedAgain()
    {
        await _shell.LookForANewVersionAsync();
        _shell.DismissNoticeCommand.Execute(null);

        await _shell.LookForANewVersionAsync();

        Assert.Equal(string.Empty, _shell.Notice);
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test.
        }
    }

    private sealed class OneRelease(PublishedRelease release) : IReleaseSource
    {
        public Task<PublishedRelease?> NewestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PublishedRelease?>(release);
    }

    private sealed class ReplaceableInstaller : IAppInstaller
    {
        public bool CanReplaceItself => true;

        public Task<string> DownloadAsync(
            ReleaseDownload download,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("nothing is downloaded in these tests");

        public void InstallAndStart(string staged) =>
            throw new InvalidOperationException("nothing is installed in these tests");

        public void CleanUp()
        {
        }
    }

    private sealed class SilentLog : IActivityLog
    {
        public void Append(string line)
        {
        }
    }
}
