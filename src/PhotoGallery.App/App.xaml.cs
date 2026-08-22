using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.Theme;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Preferences;
using PhotoGallery.Application.UseCases.Refresh;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Application.UseCases.Videos;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.App;

// PhotoGallery.Application (the layer) shadows System.Windows.Application here,
// so the base type needs its full name.
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    private IActivityLog? _log;

    /// <summary>
    /// Reports anything that would otherwise close the app without a word.
    /// </summary>
    /// <remarks>
    /// There was no handler here, and the cost of that was learned rather than
    /// guessed: a style applied to the wrong kind of control throws while the
    /// window is being parsed, and the app vanished on start-up leaving nothing
    /// but a Windows crash record. A message naming the fault, and a line in the
    /// log, turns that into something a person can act on.
    ///
    /// <para>It does not pretend to recover. A window that failed to parse has
    /// nothing to go back to, so this reports and lets it close - what it buys
    /// is knowing why.</para>
    /// </remarks>
    private void OnUnhandledException(
        object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticLog.Write("unhandled", e.Exception);

        try
        {
            _log?.Append($"unhandled: {e.Exception}");
        }
        catch (IOException)
        {
            // Failing to record the failure must not replace it.
        }

        // The one dialog in this app that is still Windows', and deliberately.
        // Everywhere else AppDialog is used so the surface matches the theme,
        // but this runs after something has already gone wrong on the dispatcher
        // - which is exactly when a WPF window is least able to be drawn. A
        // theme dictionary that failed to parse, or a template that threw while
        // rendering, would throw again here and swallow the message that
        // explains it. Windows draws this one whatever state the app is in.
        MessageBox.Show(
            $"Photo Gallery hit a problem it did not expect.\n\n{e.Exception.Message}",
            "Photo Gallery", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// Start-up needs one thing: where the working folder is. Once a library has
    /// been opened it goes straight back to it, because a screen whose answer is
    /// always the same is a toll booth rather than a question.
    /// </summary>
    /// <remarks>
    /// The picker still appears on a genuine first run, and whenever the
    /// remembered folder is no longer a library - moved, renamed, or on a drive
    /// that is not connected - because then the app really does not know where
    /// to go.
    /// </remarks>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // The one file outside any working folder: it says which folder was last
        // used and which palette to open in, neither of which can come from a
        // database that has not been located yet.
        var configStore = new JsonAppConfigStore();
        AppConfig config = configStore.Load();

        // The set-up screen has no library to ask, so it opens in the plain
        // default. Once a library is open its own stored palette takes over.
        ThemeManager.Apply(ThemePreference.Light);

        string? folder = RememberedLibrary(config);
        bool folderHasPhotos = false;

        if (folder is null)
        {
            var welcome = new WelcomeWindow(config);
            if (welcome.ShowDialog() != true || welcome.ChosenFolder is null)
            {
                Shutdown();
                return;
            }

            folder = welcome.ChosenFolder;
            folderHasPhotos = welcome.FolderHasPhotos;
        }

        // Started as soon as a folder is known and before anything is built, so
        // that a failure while the window is being put together - which is what
        // took the app down without a word once - lands in the file.
        if (config.Diagnostics)
        {
            DiagnosticLog.Start(Path.Combine(folder, "logs"));
            DiagnosticLog.Write($"opening {folder}");
        }

        _services = new ServiceCollection()
            .AddPhotoGalleryInfrastructure(folder)
            .AddScoped<OpenLibraryHandler>()
            .AddScoped<AddPhotoSourceHandler>()
            .AddScoped<RemovePhotoSourceHandler>()
            .AddScoped<ScanPhotoSourceHandler>()
            .AddScoped<BuildThumbnailsHandler>()
            .AddScoped<BuildVideoKeyframesHandler>()
            .AddScoped<DetectFacesHandler>()
            .AddScoped<IndexContentHandler>()
            .AddScoped<LocatePhotosHandler>()
            .AddScoped<FindPlacesHandler>()
            .AddScoped<SearchPhotosHandler>()
            .AddScoped<GetPeopleBoardHandler>()
            .AddScoped<FindPeopleHandler>()
            .AddScoped<TurnPhotoHandler>()
            .AddScoped<RemovePhotoHandler>()
            .AddScoped<FindDuplicatesHandler>()
            .AddScoped<GetDuplicatesHandler>()
            .AddScoped<GetPersonReviewHandler>()
            .AddScoped<ProposeFacesHandler>()
            .AddScoped<AssignFacesHandler>()
            .AddScoped<RecheckPeopleHandler>()
            .AddScoped<RefreshLibraryHandler>()
            .AddScoped<QueryGalleryHandler>()
            .AddScoped<GetFolderTreeHandler>()
            .AddScoped<SaveThemeHandler>()
            .AddScoped<SaveGalleryCellSizeHandler>()
            .AddScoped<SaveGallerySortOrderHandler>()
            .AddScoped<SaveNavigationCollapsedHandler>()
            .AddSingleton<GalleryViewModel>()
            .AddSingleton<PeopleViewModel>()
            .AddSingleton<DuplicatesViewModel>()
            .AddSingleton<MainViewModel>()
            .BuildServiceProvider();

        _log = _services.GetRequiredService<IActivityLog>();

        var viewModel = _services.GetRequiredService<MainViewModel>();
        viewModel.RestoreDiagnostics(config.Diagnostics);
        viewModel.WorkingFolder = folder;

        OpenLibraryResult result;
        try
        {
            // Step 2: create the index if this folder has none, or migrate an
            // existing one to the current schema.
            using IServiceScope scope = _services.CreateScope();
            result = await scope.ServiceProvider
                .GetRequiredService<OpenLibraryHandler>()
                .HandleAsync();
        }
        catch (Exception ex)
        {
            AppDialog.Tell(
                owner: null,
                "Could not open a library in that folder",
                ex.Message,
                DialogTone.Danger);
            Shutdown();
            return;
        }

        // Restore the palette this library was last using.
        ThemeManager.Apply(result.Theme);
        viewModel.ApplyOpenResult(result);

        // Set-up said photos already in the chosen folder would be added, so
        // offer it in the Library view rather than making the user retype it.
        if (folderHasPhotos && result.HasNoSources)
        {
            viewModel.NewSourcePath = folder;
        }

        var window = new MainWindow(viewModel);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    /// <summary>
    /// The folder last opened, if it still holds a library.
    /// </summary>
    /// <remarks>
    /// The index file is the test rather than the folder merely existing: an
    /// empty folder of the same name - left behind by a move, or recreated by
    /// something else - would otherwise have the app quietly scaffold a second,
    /// empty library over the top of where the real one used to be.
    /// </remarks>
    private static string? RememberedLibrary(AppConfig config) =>
        WorkingFolder.IsLibrary(config.LastWorkingFolder) ? config.LastWorkingFolder : null;


    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.Stop();
        _services?.Dispose();
        base.OnExit(e);
    }
}

