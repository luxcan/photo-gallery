using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.About;
using PhotoGallery.App.Albums;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.Imaging;
using PhotoGallery.App.Models;
using PhotoGallery.App.Sharing;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.Theme;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Application.UseCases.Preferences;
using PhotoGallery.Application.UseCases.Refresh;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Reads the cached copy of whichever photograph is being deleted.</summary>
    private readonly IThumbnailStore _thumbnails;

    private readonly IActivityLog _activityLog;

    [ObservableProperty]
    private ActivitySection _selectedSection;

    /// <summary>Whether the side nav is folded down to its icons.</summary>
    [ObservableProperty]
    private bool _isNavCollapsed;

    /// <summary>
    /// The fold the user asked for, which is what gets stored. Separate from
    /// <see cref="IsNavCollapsed"/> because a narrow window folds the nav
    /// without anyone having asked, and that must not be mistaken for a choice
    /// when the window is made wide again.
    /// </summary>
    private bool _chosenCollapsed;

    /// <summary>Which side of the threshold the window was last seen on.</summary>
    private bool? _windowWasNarrow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FacesAvailable), nameof(SearchHint),
        nameof(SearchUpgradeHint), nameof(HasSearchUpgrade), nameof(NeedsFaceScan),
        nameof(NeedsContentScan), nameof(ScanToApplyHint), nameof(SearchNotice),
        nameof(HasSearchNotice), nameof(CanScanToApply), nameof(RecheckHint))]
    [NotifyCanExecuteChangedFor(nameof(RecheckPeopleCommand))]
    private LibraryCounts _counts = LibraryCounts.Empty;

    /// <summary>
    /// The folder being added. Typed as well as browsed, because the Windows
    /// folder picker will not reliably select a network path that is typed into
    /// it - it navigates instead, and returns whatever was highlighted.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSourceCommand))]
    private string _newSourcePath = string.Empty;

    [ObservableProperty]
    private string _sourceError = string.Empty;

    /// <summary>
    /// What the videos still need, said beside the button that scans.
    /// </summary>
    /// <remarks>
    /// Its own line rather than <see cref="SourceError"/>, which is drawn in the
    /// danger colour because everything else that writes to it is a folder that
    /// could not be added. "1,204 videos now have a picture on them" is not a
    /// fault and must not be coloured like one.
    /// </remarks>
    [ObservableProperty]
    private string _videoStatus = string.Empty;

    /// <summary>
    /// True for the whole of one refresh - crawling and then generating - because
    /// they are one action now rather than two buttons.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsOverlayVisible), nameof(CanStopPass))]
    [NotifyCanExecuteChangedFor(nameof(ScanAllCommand), nameof(ScanSourceCommand),
        nameof(AddSourceCommand), nameof(RemoveSourceCommand), nameof(CancelPassCommand),
        nameof(RecheckPeopleCommand))]
    private bool _isRefreshing;

    /// <summary>
    /// Whether any proposal was answered one at a time while the viewer was open,
    /// so the album's own screen is rebuilt once on the way out rather than after
    /// every answer.
    /// </summary>
    private bool _decidedSuggestions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsOverlayVisible), nameof(CanStopPass))]
    [NotifyCanExecuteChangedFor(nameof(ScanAllCommand), nameof(ScanSourceCommand),
        nameof(AddSourceCommand), nameof(RemoveSourceCommand), nameof(CancelPassCommand),
        nameof(RecheckPeopleCommand))]
    private bool _isDetaching;

    /// <summary>
    /// True while faces are being looked for. Under the same overlay as the
    /// other passes: it reads the whole library, and letting it race a scan that
    /// is rewriting the same rows would have each undo the other.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsOverlayVisible), nameof(CanStopPass))]
    [NotifyCanExecuteChangedFor(nameof(ScanAllCommand), nameof(ScanSourceCommand),
        nameof(AddSourceCommand), nameof(RemoveSourceCommand), nameof(CancelPassCommand),
        nameof(RecheckPeopleCommand))]
    private bool _isDetecting;

    /// <summary>
    /// True while photographs are being deleted.
    /// </summary>
    /// <remarks>
    /// Under the same overlay as the passes, and for a stronger reason than any
    /// of them: this is the one thing in the app that destroys something, it can
    /// take minutes over a share, and until now it showed the user nothing at
    /// all. It also has to keep the passes out - a scan reading rows that are
    /// being deleted underneath it would report a library neither of them meant.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsOverlayVisible), nameof(CanStopPass))]
    [NotifyCanExecuteChangedFor(nameof(ScanAllCommand), nameof(ScanSourceCommand),
        nameof(AddSourceCommand), nameof(RemoveSourceCommand), nameof(CancelPassCommand),
        nameof(RecheckPeopleCommand))]
    private bool _isDeleting;

    /// <summary>True while an album's originals are being checked or moved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsOverlayVisible), nameof(CanStopPass))]
    [NotifyCanExecuteChangedFor(nameof(ScanAllCommand), nameof(ScanSourceCommand),
        nameof(AddSourceCommand), nameof(RemoveSourceCommand), nameof(CancelPassCommand),
        nameof(RecheckPeopleCommand))]
    private bool _isMovingAlbum;

    /// <summary>What the overlay is doing, e.g. "Indexing" or "Detaching folder".</summary>
    [ObservableProperty]
    private string _overlayTitle = string.Empty;

    /// <summary>The folder the overlay names, when it is working on one.</summary>
    [ObservableProperty]
    private string _overlayTarget = string.Empty;

    [ObservableProperty]
    private string _overlayStatus = string.Empty;

    /// <summary>
    /// Nought to a hundred rather than a fraction, because the progress bar keeps
    /// its default range.
    /// </summary>
    [ObservableProperty]
    private double _overlayPercent;

    /// <summary>
    /// True while the work has no knowable total - a crawl does not learn how
    /// many files it will find until it has found them.
    /// </summary>
    [ObservableProperty]
    private bool _overlayIsIndeterminate;

    /// <summary>
    /// The picture the overlay is working on, or null when there is none to
    /// show.
    /// </summary>
    /// <remarks>
    /// Set only by the deletion pass. The passes that read the library work on
    /// thousands of pictures a minute and a frame flickering through them would
    /// be noise; a deletion is slow, deliberate and irreversible, and seeing
    /// which photograph is going is the point.
    /// </remarks>
    [ObservableProperty]
    private ImageSource? _overlayPicture;

    /// <summary>
    /// What the overlay promises about stopping. True of the passes that read
    /// the library, and emphatically not true of the one that deletes from it.
    /// </summary>
    [ObservableProperty]
    private string _overlayHint = PassHint;

    private const string PassHint =
        "Stopping is safe at any point. Everything finished so far is kept, and "
        + "running this again carries on from where it left off. Your own photos "
        + "and videos are never changed.";

    private const string DeletingHint =
        "Stopping is safe, but it does not undo. Photographs already deleted stay "
        + "deleted; the ones not yet reached are left exactly as they are.";

    private const string MovingAlbumHint =
        "Stopping is safe, but it does not undo. Originals already moved stay in the "
        + "new folder and their library locations are updated; the rest stay where they are.";

    /// <summary>
    /// Shown once the files have gone and only the index is catching up.
    /// </summary>
    /// <remarks>
    /// Deliberately offers nothing to stop. Both other hints do, and repeating
    /// that here would invite somebody to interrupt the one part of deleting
    /// where stopping cannot help and could leave the screen disagreeing with
    /// the disk.
    /// </remarks>
    private const string SettlingHint =
        "The photographs have gone. Photo Gallery is working out what its screens "
        + "should show now, which takes a few seconds on a large library.";

    /// <summary>
    /// True once a deletion's files have gone and only the index is catching up.
    /// </summary>
    /// <remarks>
    /// Exists to take the Stop button away for that stretch. Nothing there can
    /// be stopped - the files are already gone - and a button that greys out is
    /// a truer answer than one that can be pressed and does nothing.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStopPass))]
    [NotifyCanExecuteChangedFor(nameof(CancelPassCommand))]
    private bool _isSettling;

    /// <summary>Whether there is anything left that stopping would help with.</summary>
    public bool CanStopPass => IsOverlayVisible && !IsSettling;

    /// <summary>
    /// True while a screen is rebuilding itself after a decision the user made.
    /// </summary>
    /// <remarks>
    /// Not a pass and not a deletion: nothing is being read off the share and no
    /// file is being touched. It raises the overlay for the same reason those do,
    /// which is that re-reading the duplicate sets on this library takes ten to
    /// twenty seconds and a screen that sits unchanged for that long reads as one
    /// that ignored the click.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsOverlayVisible), nameof(CanStopPass))]
    [NotifyCanExecuteChangedFor(nameof(CancelPassCommand), nameof(ScanAllCommand),
        nameof(ScanSourceCommand), nameof(AddSourceCommand), nameof(RemoveSourceCommand),
        nameof(RecheckPeopleCommand))]
    private bool _isTidying;

    private const string TidyingHint =
        "Nothing on disk is being changed. Photo Gallery is working out what its "
        + "screens should show now, which takes a few seconds on a large library.";

    private CancellationTokenSource? _refreshCancellation;

    private CancellationTokenSource? _detachCancellation;

    private CancellationTokenSource? _detectCancellation;

    private CancellationTokenSource? _deleteCancellation;

    private CancellationTokenSource? _albumMoveCancellation;

    public MainViewModel(
        IServiceScopeFactory scopeFactory,
        GalleryViewModel gallery,
        PeopleViewModel people,
        DuplicatesViewModel duplicates,
        IThumbnailStore thumbnails,
        IActivityLog activityLog)
    {
        _scopeFactory = scopeFactory;
        _thumbnails = thumbnails;
        Gallery = gallery;
        People = people;
        Duplicates = duplicates;
        _activityLog = activityLog;

        About = new AboutViewModel();
        Albums = new AlbumsViewModel(scopeFactory, thumbnails);
        Models = new ModelsViewModel(scopeFactory);
        Sharing = new SharingViewModel(scopeFactory);

        // The nav and the search box both gate on what is installed, and neither
        // of them owns it.
        Models.AvailabilityChanged += (_, _) => RefreshAvailability();

        // Glyphs are Segoe MDL2 Assets code points. Everything except Photo
        // sources needs photos before it can show anything, so it stays disabled.
        // Albums sits directly under Library because it is the same photos
        // grouped, and People follows it: the bar descends from everything,
        // through a grouping of everything, to a slice of it.
        TopSections =
        [
            new ActivitySection(ActivitySection.LibraryKey, "Library", "\uE91B", true),
            new ActivitySection(
                ActivitySection.AlbumsKey, "Albums", "\uE8FD", true),
            new ActivitySection(
                ActivitySection.PeopleKey, "People", "\uE716", true, RequiresFaces: true),
            new ActivitySection(ActivitySection.DuplicatesKey, "Duplicates", "\uE8C8", true),
            new ActivitySection(ActivitySection.SourcesKey, "Photo sources", "\uED25", false),
        ];

        // The foot of the bar is where the things that are not the library go,
        // as it is in VS Code. Settings leads them and About sits under it: the
        // two are read in the order they are wanted, and the one nobody opens
        // twice is the one at the very bottom.
        BottomSections =
        [
            // Above Settings, because it is something somebody does rather
            // than something they configure - and it needs photos to be worth
            // opening at all, which is what the flag says.
            new ActivitySection(ActivitySection.SharingKey, "Sharing", "\uE72D", true),
            new ActivitySection(ActivitySection.SettingsKey, "Settings", "\uE713", false),
            new ActivitySection(ActivitySection.AboutKey, "About", "\uE946", false),
        ];

        _selectedSection = TopSections[^1];
        Sources.CollectionChanged += (_, _) =>
        {
            RefreshSourceStates();
            ScanAllCommand.NotifyCanExecuteChanged();
        };

        // The sort control binds straight to the gallery, so unlike the zoom
        // there is no gesture to hang the save on. Applying a stored order while
        // opening comes back through here too, which is harmless only because
        // the handler ignores a value that has not changed.
        Gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GalleryViewModel.OldestFirst))
            {
                _ = RememberSortOrderAsync();
            }

            // Proposals answered one at a time are already saved; what is not
            // done is the album's own screen, which is deliberately left until
            // the viewer is out of the way rather than rebuilt behind it after
            // every answer.
            if (e.PropertyName == nameof(GalleryViewModel.IsViewerOpen)
                && !Gallery.IsViewerOpen
                && _decidedSuggestions)
            {
                _decidedSuggestions = false;
                _ = Albums.SettleAfterDecidingAsync();
            }
        };

        // The status bar counts what the library holds, and until now only a
        // pass told it anything had changed - so deleting a photograph left the
        // total sitting there unchanged, which reads as the delete not having
        // worked. Every screen that can change the library says so instead.
        Gallery.LibraryChanged += OnLibraryChanged;
        People.LibraryChanged += OnLibraryChanged;
        Duplicates.LibraryChanged += OnLibraryChanged;
        Albums.LibraryChanged += OnLibraryChanged;
    }

    /// <remarks>
    /// <c>async void</c> because it is an event handler, and the failure is
    /// caught rather than left to become an unobserved task: a count that could
    /// not be re-read is worth a line in the log and nothing more, but it must
    /// not disappear.
    /// </remarks>
    private async void OnLibraryChanged(object? sender, EventArgs e)
    {
        try
        {
            await RefreshCountsAsync();

            // The viewer is drawn over the content area, so naming a face or
            // deleting a picture inside it changes the screen underneath without
            // ever changing section - and a section change was the only thing
            // that reloaded People. Naming somebody in the viewer therefore left
            // the list behind it without them in it, and every review badge on
            // it wrong, until the user clicked away and back.
            //
            // Only for changes the viewer reported: People rebuilds itself after
            // its own, and starting a second reload underneath the first would
            // be turned away by the busy guard and lost.
            if (ShowPeople && sender is GalleryViewModel)
            {
                await People.ReloadAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            DiagnosticLog.Write("could not re-read the library counts", ex);
        }
    }

    public GalleryViewModel Gallery { get; }

    public PeopleViewModel People { get; }

    public DuplicatesViewModel Duplicates { get; }

    public AlbumsViewModel Albums { get; }

    /// <summary>
    /// Built here rather than injected, unlike its three siblings above. About
    /// reads nothing from the library and writes nothing to it, so it needs
    /// nothing from the container to be constructed with.
    /// </summary>
    public AboutViewModel About { get; }

    public ModelsViewModel Models { get; }

    /// <summary>The other computers in the house, and the button that reaches them.</summary>
    public SharingViewModel Sharing { get; }

    /// <summary>
    /// Whether faces can be worked with at all.
    /// </summary>
    /// <remarks>
    /// The models, <em>or</em> people already named. Names live in the database
    /// and not in the weights, so somebody who named forty people and then moved
    /// the model files still has forty people - and hiding the screen that shows
    /// them behind a missing optional download would be losing the user's own
    /// work to a file they can put back.
    /// </remarks>
    public bool FacesAvailable =>
        Models.IsReady(ModelFeature.Faces) || Counts.People > 0;

    public bool ContentSearchAvailable => Models.IsReady(ModelFeature.ContentSearch);

    /// <summary>
    /// The face model is here and no photograph has been looked at yet.
    /// </summary>
    /// <remarks>
    /// The state a first install lands in, and the one this app was worst at:
    /// the models are downloaded after the library has been scanned, faces are
    /// found by scanning, so People unlocks empty and stays empty. Nothing was
    /// broken and nothing said so - the screen simply had no faces, and the way
    /// to get some was to know that scanning again is what applies a model.
    ///
    /// <para>Named as a state rather than asked of the two things it is made of,
    /// so the screen can offer the scan instead of describing it.</para>
    ///
    /// <para>Pictures still to look at, never "no faces found": a library of
    /// landscapes can have been looked at properly and hold none, and this would
    /// then offer a scan that changes nothing for as long as the library
    /// exists.</para>
    /// </remarks>
    public bool NeedsFaceScan =>
        Models.IsReady(ModelFeature.Faces) && Counts.AwaitingFaces > 0;

    /// <summary>The same, for what a picture is of.</summary>
    public bool NeedsContentScan =>
        Models.IsReady(ModelFeature.ContentSearch) && Counts.AwaitingDescription > 0;

    /// <summary>
    /// An installed model that has not been applied to the library yet, said in
    /// terms of what it would do rather than which pass is outstanding.
    /// </summary>
    public string ScanToApplyHint => (NeedsFaceScan, NeedsContentScan) switch
    {
        (true, true) =>
            "The models are installed. Scan your folders to find the faces in your "
            + "pictures and read what they are of.",
        (true, false) =>
            "The face model is installed. Scan your folders to find the faces in "
            + "your pictures.",
        (false, true) =>
            "The picture-description model is installed. Scan your folders to read "
            + "what your pictures are of.",
        _ => string.Empty,
    };

    /// <summary>
    /// What stands under the search box: what is missing first, then what is
    /// installed and not yet applied.
    /// </summary>
    /// <remarks>
    /// One notice rather than two stacked, and in that order because a model that
    /// is not here cannot be applied - telling somebody to scan for a feature
    /// they have not downloaded is an instruction that cannot work.
    /// </remarks>
    public string SearchNotice =>
        HasSearchUpgrade ? SearchUpgradeHint : ScanToApplyHint;

    public bool HasSearchNotice => SearchNotice.Length > 0;

    /// <summary>Whether that notice is one a scan answers, rather than a download.</summary>
    public bool CanScanToApply => !HasSearchUpgrade && ScanToApplyHint.Length > 0;

    /// <summary>
    /// What is not installed, and what installing it would let the box answer.
    /// </summary>
    /// <remarks>
    /// Standing under the search box rather than raised after a search that
    /// could not be answered. Typing a description and only then being told it
    /// cannot be looked for wastes the one thing the user did, and it arrives
    /// reading like a fault rather than a limitation.
    ///
    /// <para>Named by what it enables, never by the file. The encoder is called
    /// ContentText, which means nothing to anybody who did not write it.</para>
    /// </remarks>
    public string SearchUpgradeHint => (FacesAvailable, ContentSearchAvailable) switch
    {
        (true, true) => string.Empty,
        (true, false) =>
            "Install the picture-description model to search by what is in a photograph.",
        (false, true) =>
            "Install the face model to find people and search by name.",
        (false, false) =>
            "Install the face and picture-description models to search by name, "
            + "or by what is in a photograph.",
    };

    public bool HasSearchUpgrade => SearchUpgradeHint.Length > 0;

    /// <summary>Opens the screen the hint points at, on the tab that answers it.</summary>
    [RelayCommand]
    private void OpenSearchSettings()
    {
        ShowsSearchSettings = true;
        SelectedSection = BottomSections.First(
            section => section.Key == ActivitySection.SettingsKey);
    }

    /// <summary>
    /// Whether Settings is showing what makes searching work rather than the
    /// library's own preferences.
    /// </summary>
    /// <remarks>
    /// Two tabs because the screen now holds two unrelated jobs: where this
    /// library lives and how it looks, against the models and the gazetteer that
    /// decide what can be searched for. The second grew large enough to push the
    /// first off the bottom of the screen, which is what made them two things.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGeneralSettings))]
    private bool _showsSearchSettings;

    /// <summary>
    /// The other side of <see cref="ShowsSearchSettings"/>, so each tab binds to
    /// a property that answers for it rather than sharing one inverted.
    /// </summary>
    public bool ShowsGeneralSettings
    {
        get => !ShowsSearchSettings;
        set => ShowsSearchSettings = !value;
    }

    /// <summary>
    /// What the search box can currently answer, as its watermark.
    /// </summary>
    /// <remarks>
    /// It narrows rather than switching off. Places come from a gazetteer
    /// compiled into the executable and work on a fresh install with nothing
    /// downloaded, so disabling the whole box would take away the one kind of
    /// search that never needed a model - and promising all three when two of
    /// them cannot answer is worse than promising one.
    /// </remarks>
    public string SearchHint => (FacesAvailable, ContentSearchAvailable) switch
    {
        (true, true) => "Name, place, or what is in the picture",
        (true, false) => "Name or place",
        (false, true) => "Place, or what is in the picture",
        (false, false) => "Place",
    };

    public IReadOnlyList<ActivitySection> TopSections { get; }

    public IReadOnlyList<ActivitySection> BottomSections { get; }


    public ObservableCollection<PhotoSourceItem> Sources { get; } = [];

    public bool HasSources => Sources.Count > 0;

    public bool HasNoSources => Sources.Count == 0;

    /// <summary>No long pass is running, so the library may be changed.</summary>
    public bool IsIdle =>
        !IsRefreshing && !IsDetaching && !IsDetecting && !IsDeleting
        && !IsMovingAlbum && !IsTidying;

    /// <summary>
    /// Whether the window is covered. Both passes rewrite the library's shape and
    /// neither may be raced by the other, so both take the shade.
    /// </summary>
    public bool IsOverlayVisible =>
        IsRefreshing || IsDetaching || IsDetecting || IsDeleting
        || IsMovingAlbum || IsTidying;

    public bool ShowSources => SelectedSection.Key == ActivitySection.SourcesKey;

    public bool ShowLibrary => SelectedSection.Key == ActivitySection.LibraryKey;

    public bool ShowSettings => SelectedSection.Key == ActivitySection.SettingsKey;

    public bool ShowPeople => SelectedSection.Key == ActivitySection.PeopleKey;

    public bool ShowAlbums => SelectedSection.Key == ActivitySection.AlbumsKey;

    public bool ShowDuplicates => SelectedSection.Key == ActivitySection.DuplicatesKey;

    public bool ShowAbout => SelectedSection.Key == ActivitySection.AboutKey;

    public bool ShowSharing => SelectedSection.Key == ActivitySection.SharingKey;

    public string SourceSummary => Sources.Count switch
    {
        0 => "no photos connected",
        1 => Sources[0].Path,
        _ => $"{Sources.Count} photo sources",
    };

    /// <summary>
    /// Where this library lives, as Settings shows it.
    /// </summary>
    /// <remarks>
    /// Set once at start-up rather than resolved per use: it cannot change while
    /// the app is running - switching library restarts it. It had been bound to
    /// and never existed, so that screen quietly showed an empty line until the
    /// diagnostic log reported the failed binding on its first run.
    /// </remarks>
    [ObservableProperty]
    private string _workingFolder = string.Empty;

    /// <summary>
    /// Whether the detailed log is being written.
    /// </summary>
    /// <remarks>
    /// Bound straight to a switch, so flicking it is the whole gesture and the
    /// setting is saved as a consequence rather than by a separate button. A
    /// button labelled with the action it performs has to be read before the
    /// state is known; a switch shows it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticsLabel))]
    private bool _diagnosticsEnabled;

    /// <summary>
    /// True while the stored setting is being put back at start-up, so that
    /// restoring it is not mistaken for the user flicking the switch.
    /// </summary>
    private bool _restoringDiagnostics;

    public string DiagnosticsLabel => DiagnosticLog.IsOn
        ? $"Writing to {DiagnosticLog.Path}"
        : DiagnosticsEnabled
            ? "On from the next start."
            : "Off. Switch this on only while chasing a problem.";

    /// <summary>Puts the stored setting on the switch without saving it back.</summary>
    public void RestoreDiagnostics(bool enabled)
    {
        _restoringDiagnostics = true;
        try
        {
            DiagnosticsEnabled = enabled;
        }
        finally
        {
            _restoringDiagnostics = false;
        }
    }

    /// <remarks>
    /// Saved beside the executable rather than in the library, because the run
    /// most worth recording is one that never reached a library. It takes effect
    /// on restart: the log has to begin before anything it would be needed to
    /// explain.
    /// </remarks>
    partial void OnDiagnosticsEnabledChanged(bool value)
    {
        if (_restoringDiagnostics)
        {
            return;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppConfigStore store = scope.ServiceProvider.GetRequiredService<IAppConfigStore>();
            store.Save(store.Load() with { Diagnostics = value });

            Append(value
                ? "diagnostic log will be written from the next start"
                : "diagnostic log switched off");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Append($"could not change the diagnostic log: {ex.Message}");
        }
    }

    /// <summary>
    /// The palette, as a switch rather than a button.
    /// </summary>
    /// <remarks>
    /// A setting that is set once and then left, so it lives in Settings with
    /// the other such things rather than on the status bar, where it sat beside
    /// the counts and looked like something to be used often.
    /// </remarks>
    public bool IsDarkTheme
    {
        get => ThemeManager.IsDark;
        set
        {
            if (value != ThemeManager.IsDark)
            {
                _ = ToggleThemeAsync();
            }
        }
    }

    public string ThemeChoiceLabel => ThemeManager.IsDark
        ? "Dark. Switch off for the light palette."
        : "Light. Switch on for the dark palette.";

    public void ApplyOpenResult(OpenLibraryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Counts = result.Counts;
        VideoStatus = VideosWaitingNote;
        Gallery.SetCellSize(result.GalleryCellSize);
        Gallery.SetSortOrder(result.GallerySortOrder);
        RestoreNavigation(result.NavigationCollapsed);

        Sources.Clear();
        foreach (PhotoSource source in result.Sources)
        {
            Sources.Add(new PhotoSourceItem(
                source.Id, source.Path, result.FileCountFor(source.Id), source.LastScanUtc));
        }

        Append(result.WasCreated
            ? $"created a new library at {result.WorkingFolder}"
            : $"opened {result.WorkingFolder}");
        Append($"index schema is current | {Counts.TotalAssets:N0} assets, "
             + $"{Counts.Faces:N0} faces, {Counts.People:N0} people");
        Append(result.HasNoSources
            ? "no photos connected yet - add a folder under Photo sources"
            : $"photo sources: {Sources.Count}");

        // Not awaited: proving the models means digesting up to 1.9 GB, and the
        // window should be usable while that happens. Everything gated on them
        // is disabled until it answers, which is the safe way round.
        _ = Models.RefreshAsync();
    }

    /// <summary>
    /// Adds the folder currently in <see cref="NewSourcePath"/>, whether it was
    /// typed, pasted or browsed to, and then indexes and prepares it.
    /// </summary>
    /// <remarks>
    /// Adding used to leave a row reading "-" files and "Never" scanned, which is
    /// a dead end: the folder is in the library but the library has nothing to
    /// show for it until the user finds a second button. One action now carries
    /// through to pictures on screen.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanAddSource))]
    private async Task AddSourceAsync()
    {
        string folder = NewSourcePath.Trim();
        SourceError = string.Empty;

        if (!Path.IsPathFullyQualified(folder))
        {
            SourceError = @"Enter a full path, for example D:\Photos or \\server\share.";
            Append($"could not add that folder: {folder} is not a full path");
            return;
        }

        PhotoSourceItem added;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<AddPhotoSourceHandler>();

            // Also off the UI thread: validating a folder means asking the file
            // system whether it exists, and a share that has gone away answers
            // that only when the network stack gives up.
            PhotoSource source = await Task
                .Run(() => handler.HandleAsync(folder))
                .ConfigureAwait(true);

            added = new PhotoSourceItem(source.Id, source.Path, 0, source.LastScanUtc);
            Sources.Add(added);
            NewSourcePath = string.Empty;
            Append($"added photo source: {source.Path}");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException
                                       or InvalidOperationException
                                       or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            SourceError = ex.Message;
            Append($"could not add that folder: {ex.Message}");
            return;
        }

        await RunRefreshAsync([added]);
    }

    private bool CanAddSource() => IsIdle && !string.IsNullOrWhiteSpace(NewSourcePath);

    /// <summary>
    /// Clears the last failure as soon as the path changes, so a stale red line
    /// cannot sit beside a folder that has since been corrected.
    /// </summary>
    partial void OnNewSourcePathChanged(string value) => SourceError = string.Empty;

    /// <summary>
    /// Detaches a folder. Every cached copy it owns is deleted and proved gone
    /// before its row goes, so the folder is only removed from the list once the
    /// working folder is genuinely clear of it.
    /// </summary>
    /// <remarks>
    /// Long enough to need the overlay: a folder of sixteen thousand pictures is
    /// thirty-two thousand files to delete. Stopping leaves the folder attached
    /// with fewer records, which detaching again finishes.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RemoveSourceAsync(PhotoSourceItem? item)
    {
        if (item is null)
        {
            return;
        }

        IsDetaching = true;
        SourceError = string.Empty;
        OverlayTitle = "Detaching folder";
        OverlayTarget = item.Path;
        OverlayPercent = 0;
        OverlayIsIndeterminate = false;
        OverlayStatus = "looking for cached copies...";
        _detachCancellation = new CancellationTokenSource();
        Append($"detaching {item.Path}");

        try
        {
            var progress = new Progress<RemovePhotoSourceProgress>(p =>
            {
                OverlayPercent = p.Fraction * 100;
                OverlayStatus = $"{p.Done:N0} of {p.Total:N0} records"
                              + (p.Failed > 0 ? $", {p.Failed:N0} still in use" : string.Empty);
            });

            using IServiceScope scope = _scopeFactory.CreateScope();
            RemovePhotoSourceResult result = await scope.ServiceProvider
                .GetRequiredService<RemovePhotoSourceHandler>()
                .HandleAsync(item.Id, progress, _detachCancellation.Token);

            Append($"  {result.Summary}");
            if (result.WasDetached)
            {
                Sources.Remove(item);
            }
            else
            {
                SourceError = result.Summary;
            }

            await RefreshCountsAsync();
            if (_galleryLoaded)
            {
                // The grid may have been showing pictures from this folder, and
                // the files behind them have just gone.
                await Gallery.LoadFoldersAsync();
                await Gallery.LoadAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            Append($"  could not detach that folder: {ex.Message}");
            SourceError = ex.Message;
        }
        finally
        {
            IsDetaching = false;
            ClearOverlay();
            _detachCancellation.Dispose();
            _detachCancellation = null;
        }
    }

    /// <summary>
    /// Which of these photographs' sources are away, or nothing when all of them
    /// can be reached.
    /// </summary>
    /// <remarks>
    /// Asked before the question is put, so an absent share is met with "nothing
    /// was changed" rather than with a confirmation that is granted and then
    /// quietly does nothing.
    ///
    /// <para>Under the overlay, and off the dispatcher, because it goes to the
    /// share: an absent one takes <b>21 seconds</b> to say so, and a window that
    /// locks up for that long on the way to a dialog nobody has seen yet reads
    /// as the app having hung.</para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> UnreachableSourcesAsync(
        IReadOnlyList<PhotoToRemove> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);

        if (photos.Count == 0)
        {
            return [];
        }

        IsDeleting = true;
        OverlayTitle = photos.Count == 1 ? "Checking the photo" : "Checking the photos";
        OverlayTarget = photos[0].SourceRoot;
        OverlayStatus = "making sure the folder can be reached...";
        OverlayIsIndeterminate = true;
        OverlayPercent = 0;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            RemovePhotoHandler handler =
                scope.ServiceProvider.GetRequiredService<RemovePhotoHandler>();

            return await Task.Run(() => handler.UnreachableSources(photos));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            // Unable to find out. Reported as unreachable rather than as fine,
            // because the whole point of this check is to refuse when it cannot
            // be sure - and every source is named, since which one failed is
            // exactly what could not be established.
            DiagnosticLog.Write("could not check whether the sources are reachable", ex);

            return
            [
                .. photos.Select(photo => photo.SourceRoot)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase),
            ];
        }
        finally
        {
            IsDeleting = false;
            ClearOverlay();
        }
    }

    /// <summary>
    /// Deletes photographs, showing which one is going as it goes.
    /// </summary>
    /// <remarks>
    /// Every way of deleting in this app ends here, so there is one screen for
    /// it rather than four - and the overlay is the one at the root of the
    /// window, which already paints over the photo viewer and the duplicate
    /// inspector alike. Deleting from the open photograph therefore covers the
    /// photograph, which is the only honest place for it: the picture underneath
    /// is the one being destroyed.
    ///
    /// <para>Off the dispatcher, because deleting a file is synchronous and on a
    /// share can take the best part of a second each - run here the window would
    /// freeze and the progress it is reporting would never paint.</para>
    ///
    /// <para>The view asks first. This is only reached once the question has
    /// been put and answered.</para>
    /// </remarks>
    /// <param name="settle">
    /// What the calling screen has left to do once the files are gone, run
    /// <em>under the same overlay</em>.
    /// </param>
    /// <remarks>
    /// The overlay used to come down when the last file went, and the screen
    /// then spent another ten to twenty seconds settling groups and re-reading
    /// the duplicate sets with nothing on screen to say so - the rows the user
    /// had just deleted sat there looking undeleted, and the window looked hung.
    /// Deleting is not finished until what the user is looking at agrees, so the
    /// overlay covers that too.
    /// </remarks>
    public async Task<PhotoRemovalResult> DeletePhotosAsync(
        IReadOnlyList<PhotoToRemove> photos,
        Func<PhotoRemovalResult, Task>? settle = null)
    {
        ArgumentNullException.ThrowIfNull(photos);

        if (photos.Count == 0)
        {
            return PhotoRemovalResult.Nothing;
        }

        IsDeleting = true;
        _deleteCancellation = new CancellationTokenSource();
        OverlayTitle = photos.Count == 1 ? "Deleting a photo" : $"Deleting {photos.Count:N0} photos";
        OverlayTarget = photos[0].FileName;
        OverlayStatus = "starting...";
        OverlayPercent = 0;
        OverlayIsIndeterminate = false;
        OverlayHint = DeletingHint;
        Append($"deleting {photos.Count:N0} photos");

        try
        {
            var progress = new Progress<PhotoRemovalProgress>(p =>
            {
                OverlayPercent = p.Fraction * 100d;
                OverlayTarget = p.FileName;
                OverlayStatus = $"{p.Done:N0} of {p.Total:N0}";

                // Read here rather than handed over already decoded: the
                // rendition is a small local file and this costs about a
                // millisecond, against a delete that is a network round trip.
                OverlayPicture = TileImageLoader.LoadTile(_thumbnails, p.ThumbnailName);
            });

            using IServiceScope scope = _scopeFactory.CreateScope();
            RemovePhotoHandler handler =
                scope.ServiceProvider.GetRequiredService<RemovePhotoHandler>();

            int[] assetIds = [.. photos.Select(photo => photo.AssetId)];
            CancellationToken token = _deleteCancellation.Token;

            PhotoRemovalResult result = await Task.Run(
                () => handler.HandleAsync(assetIds, progress, token),
                CancellationToken.None);

            Append($"  {result.Summary}");

            if (settle is not null)
            {
                // A second phase, said plainly rather than left blank. The files
                // have gone by now and nothing here can be stopped or undone, so
                // the bar stops pretending to measure and the hint stops offering
                // a choice that no longer exists.
                OverlayTitle = "Updating the library";
                OverlayTarget = string.Empty;
                OverlayStatus = result.Deleted == 1
                    ? "1 photo deleted - putting the list back together..."
                    : $"{result.Deleted:N0} photos deleted - putting the list back together...";
                OverlayIsIndeterminate = true;
                OverlayPercent = 0;
                OverlayPicture = null;
                OverlayHint = SettlingHint;
                IsSettling = true;

                try
                {
                    await settle(result);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    // Caught here rather than falling into the handler below,
                    // which answers a different question. The photographs did
                    // go; only the screen failed to catch up, and reporting that
                    // as a failed deletion would be a lie about the disk.
                    Append($"  the screen could not be brought up to date: {ex.Message}");
                    DiagnosticLog.Write("could not settle after deleting", ex);
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            // Not one file refusing - that is counted and reported. This is the
            // index itself failing, which leaves the caller unable to say what
            // went, so it says nothing rather than guessing.
            Append($"  the deletion could not be finished: {ex.Message}");
            DiagnosticLog.Write("the deletion could not be finished", ex);
            return PhotoRemovalResult.Nothing;
        }
        finally
        {
            // Put down before the overlay, and in a finally, so a screen that
            // throws while catching up cannot leave the Stop button dead for the
            // rest of the session.
            IsSettling = false;
            IsDeleting = false;
            ClearOverlay();
            _deleteCancellation.Dispose();
            _deleteCancellation = null;
        }
    }

    /// <summary>Checks an album move and chooses every final file name.</summary>
    public async Task<AlbumMovePlan> PlanAlbumMoveAsync(
        int albumId, string destinationFolder)
    {
        if (!IsIdle)
        {
            throw new InvalidOperationException("Wait for the current library operation to finish.");
        }

        IsMovingAlbum = true;
        _albumMoveCancellation = new CancellationTokenSource();
        OverlayTitle = "Checking the album";
        OverlayTarget = destinationFolder;
        OverlayStatus = "checking originals and destination names...";
        OverlayPercent = 0;
        OverlayIsIndeterminate = true;
        OverlayPicture = null;
        OverlayHint = MovingAlbumHint;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            MoveAlbumFilesHandler handler = scope.ServiceProvider
                .GetRequiredService<MoveAlbumFilesHandler>();
            CancellationToken token = _albumMoveCancellation.Token;

            return await Task.Run(
                () => handler.PlanAsync(albumId, destinationFolder, token),
                CancellationToken.None);
        }
        finally
        {
            IsMovingAlbum = false;
            ClearOverlay();
            _albumMoveCancellation.Dispose();
            _albumMoveCancellation = null;
        }
    }

    /// <summary>Runs a confirmed album move and refreshes every path-based view.</summary>
    public async Task<AlbumMoveResult> MoveAlbumAsync(AlbumMovePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!IsIdle)
        {
            throw new InvalidOperationException("Wait for the current library operation to finish.");
        }

        if (plan.Items.Count == 0)
        {
            AlbumMoveResult nothing = AlbumMoveResult.Nothing(plan.AlreadyThere);
            await Albums.SettleAfterOriginalsMovedAsync(nothing.Summary);
            return nothing;
        }

        IsMovingAlbum = true;
        _albumMoveCancellation = new CancellationTokenSource();
        OverlayTitle = plan.Items.Count == 1
            ? "Moving 1 original"
            : $"Moving {plan.Items.Count:N0} originals";
        OverlayTarget = plan.DestinationFolder;
        OverlayStatus = "starting...";
        OverlayPercent = 0;
        OverlayIsIndeterminate = false;
        OverlayPicture = null;
        OverlayHint = MovingAlbumHint;
        Append($"moving album \"{plan.AlbumName}\" to {plan.DestinationFolder}");

        try
        {
            var progress = new Progress<AlbumMoveProgress>(p =>
            {
                OverlayTarget = p.FileName;
                OverlayStatus = $"{p.Done:N0} of {p.Total:N0}";
                OverlayPercent = p.Fraction * 100d;
            });

            using IServiceScope scope = _scopeFactory.CreateScope();
            MoveAlbumFilesHandler handler = scope.ServiceProvider
                .GetRequiredService<MoveAlbumFilesHandler>();
            CancellationToken token = _albumMoveCancellation.Token;

            AlbumMoveResult result = await Task.Run(
                () => handler.HandleAsync(plan, progress, token),
                CancellationToken.None);

            Append($"  {result.Summary}");
            foreach (string error in result.Errors.Take(10))
            {
                Append($"  {error}");
            }

            IsSettling = true;
            OverlayTitle = "Updating the library";
            OverlayTarget = string.Empty;
            OverlayStatus = "putting folders and albums back together...";
            OverlayIsIndeterminate = true;
            OverlayPercent = 0;
            OverlayHint = SettlingHint;

            try
            {
                await Albums.SettleAfterOriginalsMovedAsync(result.Summary);
                if (_galleryLoaded)
                {
                    await Gallery.LoadFoldersAsync();
                    await Gallery.LoadAsync();
                }
            }
            catch (Exception ex) when (ex is IOException
                                           or InvalidOperationException
                                           or UnauthorizedAccessException)
            {
                // The originals and their database paths are already settled.
                // A view failing to re-read must not be reported as a failed
                // move, which would encourage somebody to try the files again.
                Append($"  the screens could not be brought up to date: {ex.Message}");
                DiagnosticLog.Write("could not settle after moving album originals", ex);
                Albums.Status = result.Summary
                    + " Reopen the album if the old folders are still shown.";
            }

            return result;
        }
        finally
        {
            IsSettling = false;
            IsMovingAlbum = false;
            ClearOverlay();
            _albumMoveCancellation.Dispose();
            _albumMoveCancellation = null;
        }
    }

    /// <summary>
    /// Runs a screen's own slow work under the shared overlay.
    /// </summary>
    /// <remarks>
    /// For the decisions that change one row and then cost seconds putting a
    /// screen back together. Settling a duplicate group is instant; re-reading
    /// every group afterwards is not, and doing that with nothing on screen left
    /// the group sitting there as though the click had been ignored.
    ///
    /// <para>Always indeterminate and never stoppable. There is no unit of
    /// progress to report - it is one query that takes as long as it takes - and
    /// interrupting a screen half way through rebuilding itself could only leave
    /// it disagreeing with the index.</para>
    /// </remarks>
    public async Task UnderOverlayAsync(string title, string status, Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        IsTidying = true;
        IsSettling = true;
        OverlayTitle = title;
        OverlayTarget = string.Empty;
        OverlayStatus = status;
        OverlayIsIndeterminate = true;
        OverlayPercent = 0;
        OverlayPicture = null;
        OverlayHint = TidyingHint;

        try
        {
            await work();
        }
        finally
        {
            IsSettling = false;
            IsTidying = false;
            ClearOverlay();
        }
    }

    /// <summary>
    /// Settles a duplicate group by keeping every copy in it.
    /// </summary>
    /// <remarks>
    /// Here rather than on the duplicates screen because of what it costs: the
    /// decision itself is one statement, and reading every group back afterwards
    /// is ten to twenty seconds on this library. That needs the overlay, and the
    /// overlay is the shell's.
    /// </remarks>
    [RelayCommand]
    private Task KeepEverythingAsync(DuplicateSetItem? set) =>
        set is null
            ? Task.CompletedTask
            : UnderOverlayAsync(
                "Keeping every copy",
                "putting the list back together...",
                () => Duplicates.KeepEverythingAsync(set));

    /// <summary>Stops whichever pass is running. All of them stop gracefully.</summary>
    [RelayCommand(CanExecute = nameof(CanStopPass))]
    private void CancelPass()
    {
        _detachCancellation?.Cancel();
        _refreshCancellation?.Cancel();
        _detectCancellation?.Cancel();
        _deleteCancellation?.Cancel();
        _albumMoveCancellation?.Cancel();
        OverlayStatus = "stopping...";
    }

    /// <summary>
    /// Applies everything the user has said about who is who to the whole
    /// library again.
    /// </summary>
    /// <remarks>
    /// The button that pays off pointing at faces in pictures. It touches no
    /// confirmation and no rejection - only the proposals, which were a question
    /// rather than a record.
    /// </remarks>
    /// <summary>
    /// There is something to look through, and no pass is running.
    /// </summary>
    /// <remarks>
    /// Faces first. This button and the scan sound alike to anybody who has not
    /// read the code - "check everyone" is a fair description of what somebody
    /// wants when their people screen is empty - and pressing it before anything
    /// has been looked at answers "nobody has been named yet", which sounds like
    /// a refusal rather than a wrong order of operations. Off until there is
    /// something for it to work with, with the reason on the button.
    /// </remarks>
    private bool CanRecheckPeople() => IsIdle && Counts.Faces > 0;

    /// <summary>What that button does, or why it cannot yet.</summary>
    public string RecheckHint => Counts.Faces > 0
        ? "Look through every picture again for the people you have named"
        : "Nothing has been looked at for faces yet - choose Find faces now first";

    [RelayCommand(CanExecute = nameof(CanRecheckPeople))]
    private async Task RecheckPeopleAsync()
    {
        IsDetecting = true;
        _detectCancellation = new CancellationTokenSource();

        OverlayTitle = "Looking for the people you have named";
        OverlayTarget = "across every picture";
        OverlayStatus = "starting...";
        OverlayIsIndeterminate = true;
        OverlayPercent = 0;

        try
        {
            var progress = new Progress<RecheckProgress>(report =>
            {
                OverlayIsIndeterminate = report.Total == 0;
                OverlayPercent = report.Fraction * 100d;
                OverlayTarget = report.Person;
                OverlayStatus =
                    $"{report.Done:N0} of {report.Total:N0} people, {report.Proposed:N0} to look at";
            });

            using IServiceScope scope = _scopeFactory.CreateScope();
            RecheckPeopleHandler handler =
                scope.ServiceProvider.GetRequiredService<RecheckPeopleHandler>();
            CancellationToken token = _detectCancellation.Token;

            RecheckResult result = await Task
                .Run(() => handler.HandleAsync(progress, token), CancellationToken.None)
                .ConfigureAwait(true);

            Append($"people: {result.Summary}");
            People.Status = result.Summary;

            await RefreshCountsAsync().ConfigureAwait(true);
            await People.RefreshAfterDetectionAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            People.Status = "Stopped. What was already found is kept.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or UnauthorizedAccessException)
        {
            Append($"could not check the people: {ex.Message}");
            People.Status = ex.Message;
        }
        finally
        {
            IsDetecting = false;
            ClearOverlay();
            _detectCancellation.Dispose();
            _detectCancellation = null;
        }
    }

    private void ClearOverlay()
    {
        OverlayTitle = string.Empty;
        OverlayTarget = string.Empty;
        OverlayStatus = string.Empty;
        OverlayPercent = 0;
        OverlayIsIndeterminate = false;
        OverlayPicture = null;
        OverlayHint = PassHint;
    }

    /// <summary>Refreshes one folder, so a single source can be brought up to date.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ScanSourceAsync(PhotoSourceItem? item) =>
        item is null ? Task.CompletedTask : RunRefreshAsync([item]);

    [RelayCommand(CanExecute = nameof(CanScanAll))]
    private Task ScanAllAsync() => RunRefreshAsync([.. Sources]);

    private bool CanScanAll() => IsIdle && HasSources;

    /// <summary>
    /// One pass: crawl the folders and reconcile them against the index, then
    /// make the renditions that turned out to be missing.
    /// </summary>
    /// <remarks>
    /// Both halves under one flag and one overlay, because they are one action -
    /// on its own, crawling produces rows with nothing to look at. The generating
    /// half is the expensive one, about an hour over a slow share on this
    /// library, which is why it can be stopped at any point: every picture it has
    /// already made keeps its status, so running this again carries on instead of
    /// starting over.
    /// </remarks>
    private async Task RunRefreshAsync(IReadOnlyList<PhotoSourceItem> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        IsRefreshing = true;
        SourceError = string.Empty;
        OverlayTitle = "Indexing";
        OverlayTarget = targets.Count == 1 ? targets[0].Path : $"{targets.Count:N0} folders";
        OverlayPercent = 0;
        OverlayIsIndeterminate = true;
        OverlayStatus = "looking for photos and videos...";
        _refreshCancellation = new CancellationTokenSource();
        Append(targets.Count == 1
            ? $"refreshing {targets[0].Path}"
            : $"refreshing {targets.Count:N0} folders");

        foreach (PhotoSourceItem item in targets)
        {
            item.IsScanning = true;
        }

        try
        {
            // Reported on the UI thread by Progress<T>, which captures the
            // context it was created on.
            var progress = new Progress<RefreshProgress>(p =>
            {
                OverlayIsIndeterminate = p.IsIndeterminate;
                OverlayPercent = p.Fraction * 100;

                if (p.Phase == RefreshPhase.Indexing)
                {
                    OverlayTitle = "Indexing";

                    // The folder, on the line that names what is being worked
                    // on - the same line the other phases use. A crawl has no
                    // total to fill a bar with, so watching the folder change is
                    // the only way to tell it apart from a crawl that has hung.
                    OverlayTarget = p.Target;
                    OverlayStatus = $"{p.Done:N0} files seen";
                }
                else if (p.Phase == RefreshPhase.PreparingVideos)
                {
                    // The phase that used to be a button. Named in the user's
                    // terms rather than as "keyframes", and given the estimate
                    // as soon as there is one, because this is the one measured
                    // in hours and the Stop underneath it is only a real choice
                    // if the screen says how much is left.
                    OverlayTitle = "Taking pictures out of your videos";
                    OverlayTarget = "so they show up like photographs do";

                    string failed = p.Failed > 0
                        ? $", {p.Failed:N0} would not open"
                        : string.Empty;

                    OverlayStatus = p.Remaining is TimeSpan togo
                        ? $"{p.Done:N0} of {p.Total:N0} videos{failed}, "
                          + $"about {togo.TotalMinutes:N0} min left"
                        : $"{p.Done:N0} of {p.Total:N0} videos{failed}";
                }
                else if (p.Phase == RefreshPhase.Collecting)
                {
                    // Its own arm, and not optional: the chain below ends in the
                    // generating branch, so a phase without one would paint
                    // "Preparing pictures" and set off a gallery reload on every
                    // report - thousands of tiles rebuilt by a phase that
                    // changed none of them.
                    OverlayTitle = "Grouping your photos into albums";
                    OverlayTarget = "so a weekend away opens as one thing";
                    OverlayStatus = p.Total == 0
                        ? "reading the dates..."
                        : $"{p.Done:N0} of {p.Total:N0} groups";
                }
                else if (p.Phase == RefreshPhase.FindingFaces)
                {
                    OverlayTitle = "Finding faces";
                    OverlayTarget = "in everything that now has a picture";
                    OverlayStatus = $"{p.Done:N0} of {p.Total:N0} pictures"
                                  + (p.Failed > 0 ? $", {p.Failed:N0} could not be read"
                                                  : string.Empty);
                }
                else if (p.Phase == RefreshPhase.Locating)
                {
                    OverlayTitle = "Working out where photos were taken";
                    OverlayTarget = "so you can search for them by place";
                    OverlayStatus = p.Remaining is TimeSpan away
                        ? $"{p.Done:N0} of {p.Total:N0} photos, "
                          + $"about {away.TotalMinutes:N0} min left"
                        : $"{p.Done:N0} of {p.Total:N0} photos";
                }
                else if (p.Phase == RefreshPhase.Describing)
                {
                    // Named for what it is rather than folded into preparing.
                    // After the first run this only touches the pictures the
                    // scan just brought in, so it usually passes in seconds -
                    // but the first one is the whole library and the user should
                    // be able to see which phase is taking the time.
                    OverlayTitle = "Learning what the pictures are of";

                    // Corrected here, because the folder named at the start of a
                    // refresh is the one being crawled and this phase does not
                    // go near it. Leaving the share's address up for an hour
                    // says the network is being hammered when what is actually
                    // being read is the copies already on this machine.
                    OverlayTarget = "from the previews on this computer";
                    OverlayStatus = p.Remaining is TimeSpan left
                        ? $"{p.Done:N0} of {p.Total:N0} pictures, about {left.TotalMinutes:N0} min left"
                        : $"{p.Done:N0} of {p.Total:N0} pictures";
                }
                else
                {
                    OverlayTitle = "Preparing pictures";
                    OverlayStatus = $"{p.Done:N0} of {p.Total:N0} pictures"
                                  + (p.Failed > 0 ? $", {p.Failed:N0} skipped" : string.Empty);

                    // Each report also gives the grid a chance to pick up what has
                    // just been written, so pictures appear while the pass runs
                    // rather than only when it finishes.
                    if (_galleryLoaded)
                    {
                        _ = Gallery.RefreshMissingPicturesAsync();
                    }
                }
            });

            using IServiceScope scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<RefreshLibraryHandler>();
            int[] ids = [.. targets.Select(target => target.Id)];
            CancellationToken token = _refreshCancellation.Token;

            // Off the UI thread, and not optional. The crawl is synchronous
            // directory I/O - 219 folders and 16,225 files over a share is about
            // forty-five seconds - and the database calls around it are SQLite,
            // which usually completes without ever yielding. ConfigureAwait only
            // moves work off this thread when a task actually suspends, so
            // without this the whole pass runs on the dispatcher: the window
            // stops painting, the overlay it was just told to show never
            // appears, and Windows marks the app Not Responding.
            //
            // Progress<T> was built on the UI thread and captured its context,
            // so reports still arrive back here.
            RefreshResult result = await Task
                .Run(() => handler.HandleAsync(ids, progress, token), CancellationToken.None)
                .ConfigureAwait(true);

            ApplyScanResults(targets, result);
            Append($"  {result.Summary}");

            await RefreshCountsAsync();

            // What the scan left undone with the videos, if it was stopped part
            // way through them.
            VideoStatus = VideosWaitingNote;

            // The scan now finds the faces, so the screen that shows them has to
            // be told - it was only ever refreshed by the button that used to do
            // this, and without this People sits on the groupings it read when it
            // was opened.
            if (result.Faces is { } faces)
            {
                People.Status = faces.Summary;

                if (faces.FacesFound > 0)
                {
                    await People.RefreshAfterDetectionAsync().ConfigureAwait(true);
                }
            }

            if (_galleryLoaded && result.ChangedAnything)
            {
                await Gallery.LoadFoldersAsync();
                await Gallery.LoadAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            Append($"  refresh failed: {ex.Message}");
            SourceError = ex.Message;
        }
        finally
        {
            foreach (PhotoSourceItem item in targets)
            {
                item.IsScanning = false;
            }

            IsRefreshing = false;
            ClearOverlay();
            _refreshCancellation.Dispose();
            _refreshCancellation = null;
        }
    }

    /// <summary>
    /// Writes each folder's row from what its crawl found. A folder that could
    /// not be reached keeps the count and the date it already had, because
    /// nothing about it was established.
    /// </summary>
    private static void ApplyScanResults(
        IReadOnlyList<PhotoSourceItem> targets, RefreshResult result)
    {
        foreach (ScanResult scan in result.Scans)
        {
            PhotoSourceItem? item = targets.FirstOrDefault(t => t.Id == scan.PhotoSourceId);
            if (item is null)
            {
                continue;
            }

            item.IsUnavailable = scan.WasUnavailable;
            if (scan.WasUnavailable)
            {
                continue;
            }

            // Seen plus whatever an unreadable folder protected: the rows this
            // source holds now, whether the walk finished or fell short.
            item.PhotoCount = scan.Indexed;
            if (!scan.WasCancelled)
            {
                item.LastScanUtc = DateTime.UtcNow;
            }
        }
    }

    private async Task RefreshCountsAsync()
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        Counts = await scope.ServiceProvider
            .GetRequiredService<ILibraryIndex>()
            .GetCountsAsync();
    }

    /// <summary>
    /// How much of the video half of the library is still waiting, as a sentence
    /// for the screen the scan is started from.
    /// </summary>
    /// <remarks>
    /// Scanning prepares videos as its second-to-last phase, so what this line
    /// reports is a scan that was stopped before it got through them - which, on
    /// a library of thousands of clips, is most of them. Without it the screen
    /// said nothing at all: the videos were indexed, they were not in the grid
    /// because they have no picture to draw, and there was no way to tell that
    /// from having none.
    ///
    /// <para>A clip that will not decode is counted apart from both, and that
    /// distinction is the whole reason this reads honestly. Such a clip keeps a
    /// null rendition name for ever and is never offered to the pass again, so
    /// counting it as waiting - which the row on its own says - had this line
    /// promising a rescan that was never going to come, and made "all of them
    /// have a picture" a sentence no real library could ever reach.</para>
    ///
    /// <para>Read where the count is established rather than from a handler on
    /// <see cref="Counts"/>, so that the line a scan writes about its own run -
    /// which says more than a count can - is not overwritten by a total re-read
    /// a moment later.</para>
    /// </remarks>
    private string VideosWaitingNote => Counts switch
    {
        { Videos: 0 } => string.Empty,

        { VideosWaiting: 0, VideosUnreadable: 0 } =>
            $"All {Counts.Videos:N0} videos have a picture.",

        { VideosWaiting: 0, VideosUnreadable: var stuck } =>
            $"All {Counts.Videos - stuck:N0} videos have a picture. The other "
            + $"{stuck:N0} will not open on this computer.",

        { VideosWaiting: var waiting } =>
            $"{waiting:N0} of {Counts.Videos:N0} videos have no picture yet, so they are "
            + "not in your library. Scanning carries on with them from here.",
    };

    /// <summary>
    /// Records what the app just did. Goes to a file under the library's
    /// <c>logs\</c> rather than to a panel, which the window no longer carries.
    /// </summary>
    public void Append(string line) => _activityLog.Append(line);

    /// <summary>
    /// Clears the remembered folder so the next start asks which library to
    /// open. The library itself is untouched, and stays in the recents list.
    /// </summary>
    public void ForgetLibrary()
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAppConfigStore>().ForgetLastFolder();
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        ThemeManager.Toggle();
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeChoiceLabel));

        try
        {
            // The palette belongs to the library, and is stored with it. There
            // is no second copy: the only window that opens before a library is
            // the set-up screen, and that only appears when there is no library
            // whose preference could be honoured.
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<SaveThemeHandler>()
                .HandleAsync(ThemeManager.Current);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The theme is already applied; failing to remember it is not worth
            // interrupting the user for.
            Append($"could not save the theme preference: {ex.Message}");
        }
    }

    /// <summary>
    /// Remembers the zoom the user just chose, alongside the palette and for the
    /// same reason: it describes this library, so it travels with it.
    /// </summary>
    /// <remarks>
    /// Called by the gesture rather than by the property changing, so applying a
    /// stored size while opening a library cannot write it straight back.
    /// </remarks>
    public async Task RememberCellSizeAsync()
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<SaveGalleryCellSizeHandler>()
                .HandleAsync(Gallery.CellSize);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The grid is already zoomed; failing to remember it is not worth
            // interrupting the user for.
            Append($"could not save the gallery zoom: {ex.Message}");
        }
    }

    /// <summary>Remembers which way round the user set the grid.</summary>
    public async Task RememberSortOrderAsync()
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<SaveGallerySortOrderHandler>()
                .HandleAsync(Gallery.SortOrder);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The grid is already re-sorted; failing to remember it is not worth
            // interrupting the user for.
            Append($"could not save the gallery order: {ex.Message}");
        }
    }

    /// <summary>Puts the stored fold back without writing it out again.</summary>
    public void RestoreNavigation(bool collapsed)
    {
        _chosenCollapsed = collapsed;
        IsNavCollapsed = collapsed;
    }

    /// <summary>
    /// Tells the nav how much room the window has.
    /// </summary>
    /// <remarks>
    /// Acts on the crossing rather than on the width, so dragging the edge about
    /// inside one side of the threshold leaves whatever the user chose alone.
    /// Coming back out of a narrow window restores their choice rather than
    /// forcing the nav open, or somebody who folded it deliberately would have
    /// it reopened by a resize they made for another reason.
    ///
    /// <para>The first measurement is the window opening rather than a crossing:
    /// the stored answer has already decided, and a narrow window can only
    /// tighten it.</para>
    /// </remarks>
    public void AdaptNavigationToWidth(double windowWidth)
    {
        if (double.IsNaN(windowWidth) || windowWidth <= 0d)
        {
            return;
        }

        bool narrow = NavLayout.IsNarrow(windowWidth);
        if (_windowWasNarrow == narrow)
        {
            return;
        }

        bool opening = _windowWasNarrow is null;
        _windowWasNarrow = narrow;

        IsNavCollapsed = opening
            ? IsNavCollapsed || narrow
            : narrow || _chosenCollapsed;
    }

    /// <summary>
    /// Folds the nav, or unfolds it. The only thing that counts as the user
    /// having an opinion, and so the only thing that is written down.
    /// </summary>
    [RelayCommand]
    private async Task ToggleNavigationAsync()
    {
        _chosenCollapsed = !IsNavCollapsed;
        IsNavCollapsed = _chosenCollapsed;
        await RememberNavigationAsync();
    }

    /// <summary>Remembers how wide the user left the nav.</summary>
    public async Task RememberNavigationAsync()
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<SaveNavigationCollapsedHandler>()
                .HandleAsync(_chosenCollapsed);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The nav has already moved; failing to remember it is not worth
            // interrupting the user for.
            Append($"could not save the navigation width: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SelectSection(ActivitySection? section)
    {
        if (section is null)
        {
            return;
        }

        // Clicking the section you are already on turns the row's own IsChecked
        // off before the command runs, and the binding that fills the row will
        // not put it back until the selection changes - which it just did not.
        // Saying it again is what restores the fill and the rail; without this
        // the open section could be clicked into looking closed.
        if (section == SelectedSection)
        {
            OnPropertyChanged(nameof(SelectedSection));
            return;
        }

        SelectedSection = section;
    }

    /// <summary>
    /// Opens one of the selected person's pictures in the shared viewer.
    /// </summary>
    /// <remarks>
    /// Wired here because the two screens are siblings and this is where they
    /// meet. The viewer has to be told which grid the picture came from, or its
    /// arrows and arrow keys look for it in the library, fail to find it, and do
    /// nothing at all.
    /// </remarks>
    [RelayCommand]
    private void OpenPersonPhoto(GalleryTile? tile) => Gallery.OpenFrom(People.Photos, tile);

    /// <summary>
    /// Opens one of an album's photographs, stepping through that album.
    /// </summary>
    /// <remarks>
    /// The same wiring the People screen needs, and for the same reason: told
    /// nothing, the viewer's arrows would walk the library instead of the twelve
    /// pictures the user is actually looking at.
    /// </remarks>
    [RelayCommand]
    private void OpenAlbumPhoto(GalleryTile? tile) =>
        Gallery.OpenFrom(Albums.Photos, tile);

    /// <summary>
    /// Opens one of an album's proposals, stepping through the proposals.
    /// </summary>
    /// <remarks>
    /// Its own command rather than the one above, because the two grids are
    /// different lists: the arrows over a proposal must walk the other
    /// proposals, not the photographs already in the album.
    /// </remarks>
    [RelayCommand]
    private void OpenSuggestedPhoto(GalleryTile? tile) =>
        Gallery.OpenSuggestion(Albums.SuggestionGrid, tile);

    /// <summary>Puts the proposal on screen into the album.</summary>
    [RelayCommand]
    private Task KeepSuggestedPhoto() => DecideOpenSuggestionAsync(keep: true);

    /// <summary>Refuses the proposal on screen, for good.</summary>
    [RelayCommand]
    private Task RefuseSuggestedPhoto() => DecideOpenSuggestionAsync(keep: false);

    /// <summary>
    /// Answers the open proposal and moves on to the next one.
    /// </summary>
    /// <remarks>
    /// Here rather than in either view model, because it is the one place that
    /// holds both: the album commits the answer and drops the proposal, and the
    /// viewer has to be told what took its place. Stepping to the same index -
    /// not the next one - is what makes a run of these feel like a queue: the
    /// list shortened underneath, so the picture that moved up is the next
    /// question.
    /// </remarks>
    private async Task DecideOpenSuggestionAsync(bool keep)
    {
        if (!Gallery.IsDecidingSuggestion || Gallery.OpenTile is not GalleryTile tile)
        {
            return;
        }

        int at = Albums.SuggestionGrid.IndexOf(tile);

        if (!await Albums.DecideSuggestionAsync(tile, keep).ConfigureAwait(true))
        {
            return;
        }

        _decidedSuggestions = true;

        if (Albums.SuggestionGrid.Count == 0)
        {
            // Closing raises IsViewerOpen, which settles the album's screen.
            Gallery.ClosePhoto();
            return;
        }

        Gallery.OpenSuggestion(
            Albums.SuggestionGrid,
            Albums.SuggestionGrid[
                Math.Clamp(at, 0, Albums.SuggestionGrid.Count - 1)]);
    }

    /// <summary>
    /// Tells everything that gates on an optional model to look again.
    /// </summary>
    /// <remarks>
    /// Called when the models change and when the counts do: naming the first
    /// person in the photo viewer is what makes the People section reachable on
    /// a library that has faces but no longer has the weights.
    /// </remarks>
    private void RefreshAvailability()
    {
        OnPropertyChanged(nameof(FacesAvailable));
        OnPropertyChanged(nameof(ContentSearchAvailable));
        OnPropertyChanged(nameof(SearchHint));
        OnPropertyChanged(nameof(SearchUpgradeHint));
        OnPropertyChanged(nameof(HasSearchUpgrade));
        OnPropertyChanged(nameof(NeedsFaceScan));
        OnPropertyChanged(nameof(NeedsContentScan));
        OnPropertyChanged(nameof(ScanToApplyHint));
        OnPropertyChanged(nameof(SearchNotice));
        OnPropertyChanged(nameof(HasSearchNotice));
        OnPropertyChanged(nameof(CanScanToApply));
    }

    partial void OnSelectedSectionChanged(ActivitySection value)
    {
        // The viewer is drawn over the content area rather than inside Library,
        // so it survives a section change and overlaps whatever loads behind it.
        Gallery.ClosePhoto();

        OnPropertyChanged(nameof(ShowSources));
        OnPropertyChanged(nameof(ShowLibrary));
        OnPropertyChanged(nameof(ShowSettings));
        OnPropertyChanged(nameof(ShowPeople));
        OnPropertyChanged(nameof(ShowAlbums));
        OnPropertyChanged(nameof(ShowDuplicates));
        OnPropertyChanged(nameof(ShowAbout));
        OnPropertyChanged(nameof(ShowSharing));

        if (ShowLibrary)
        {
            _ = ShowGalleryAsync();
        }
        else if (ShowPeople)
        {
            // Every time, not once. Naming happens in the photo viewer, so this
            // screen is stale the moment a face is named somewhere else - which
            // is how a person added a minute ago failed to appear at all.
            _ = People.ReloadAsync();
        }
        else if (ShowAlbums)
        {
            // Rebuilt on every visit, like the two screens above: a photograph
            // put into an album from the viewer changes what belongs here.
            Albums.Reopened();
            _ = Albums.ReloadAsync();
        }
        else if (ShowDuplicates)
        {
            // Rebuilt on every visit, as People is and for the same reason: a
            // photo deleted or turned somewhere else changes what belongs here.
            _ = Duplicates.ReloadAsync();
        }
        else if (ShowSettings)
        {
            // Cheap after the first: the store answers from what it already
            // proved unless a file has changed underneath it.
            Models.Reopened();
            _ = Models.RefreshAsync();
        }
        else if (ShowSharing)
        {
            // Read on every open rather than cached. It is a directory listing
            // and a count, and the thing it reports - who has shared, and when -
            // is exactly what changes while this screen is closed.
            _ = Sharing.RefreshAsync();
        }
        else if (ShowAbout)
        {
            About.Reopened();
        }
    }

    /// <summary>
    /// Loads the Library view the first time it is opened. Later visits reuse
    /// what is already laid out, so switching sections is instant.
    /// </summary>
    private async Task ShowGalleryAsync()
    {
        if (_galleryLoaded)
        {
            return;
        }

        _galleryLoaded = true;
        try
        {
            await Gallery.LoadFoldersAsync();
            await Gallery.LoadAsync();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _galleryLoaded = false;
            Append($"could not open the library view: {ex.Message}");
        }
    }

    private bool _galleryLoaded;

    private void RefreshSourceStates()
    {
        // Detaching the last source strands the user on a section that can no
        // longer show anything, so fall back to Photo sources.
        if (!HasSources && SelectedSection.RequiresSources)
        {
            SelectedSection = TopSections[^1];
        }

        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasNoSources));
        OnPropertyChanged(nameof(SourceSummary));
    }
}
