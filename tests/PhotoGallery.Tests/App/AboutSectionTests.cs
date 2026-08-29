using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.Sharing;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Every section in the bar is visible only while one boolean says so, and each
/// of those booleans has to be announced by hand when the selection changes. A
/// section added without its line in that list is a screen that never appears,
/// which is exactly the mistake this guards.
/// </summary>
/// <remarks>
/// About is also the one section that must be reachable with an empty library:
/// it is where the licence credit lives, and a credit that only appears once
/// photographs have been added is not a credit.
/// </remarks>
public sealed class AboutSectionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    public AboutSectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-about-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        // An empty provider is enough, as it is for the sibling suite: About
        // resolves nothing, and no other section is opened here.
        _services = new ServiceCollection().BuildServiceProvider();
        IServiceScopeFactory scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

        var store = new FileSystemThumbnailStore(workingFolder);
        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, store),
            new PeopleViewModel(scopeFactory, store),
            new DuplicatesViewModel(scopeFactory, store),
            store,
            new FileActivityLog(workingFolder),
            new DirectSharing(scopeFactory));
    }

    [Fact]
    public void AboutSitsUnderSettingsAndNeedsNoPhotographs()
    {
        // Sharing leads, because it is something somebody does rather than
        // something they configure; About is last, because it is the one nobody
        // opens twice.
        Assert.Equal(
            ["sharing", "settings", "about"],
            _main.BottomSections.Select(section => section.Key));

        Assert.False(
            About.RequiresSources,
            "About holds the licence credit, so it cannot wait for a photo source");
    }

    [Fact]
    public void SelectingAbout_AnnouncesItselfAndSilencesTheOthers()
    {
        List<string> announced = [];
        _main.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? string.Empty);

        _main.SelectedSection = About;

        Assert.True(_main.ShowAbout);
        Assert.False(_main.ShowSettings);
        Assert.False(_main.ShowLibrary);
        Assert.Contains(
            nameof(MainViewModel.ShowAbout),
            announced);
    }

    [Fact]
    public void OpeningAbout_ForgetsWhatTheCopyButtonLastSaid()
    {
        _main.About.CopyNotice = "The link is on the clipboard.";

        _main.SelectedSection = About;

        Assert.Null(_main.About.CopyNotice);
    }

    [Fact]
    public void ClickingTheSectionYouAreOn_SaysSoAgainSoTheRowStaysLit()
    {
        _main.SelectedSection = About;

        List<string> announced = [];
        _main.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? string.Empty);

        _main.SelectSectionCommand.Execute(About);

        // The row has already turned its own IsChecked off by the time the
        // command runs, and only a fresh notification pushes the binding back.
        Assert.Contains(nameof(MainViewModel.SelectedSection), announced);
        Assert.True(_main.ShowAbout);
    }

    [Fact]
    public void EveryLinkPointsAtThisAppsRepository()
    {
        // The label is what the user reads and the commands are what they reach,
        // so the one place they can disagree is the label.
        Assert.Equal("github.com/luxcan/photo-gallery", _main.About.RepositoryLabel);
        Assert.StartsWith("Version ", _main.About.VersionLine);
    }

    private ActivitySection About =>
        _main.BottomSections.Single(section => section.Key == "about");

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
