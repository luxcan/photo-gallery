using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Models;

namespace PhotoGallery.App.Models;

/// <summary>
/// The optional models: what each one unlocks, whether it is here, and where it
/// is kept.
/// </summary>
/// <remarks>
/// The app ships without weights. They are 1.9 GB, and the face pack is licensed
/// for non-commercial research use only, so neither can travel inside the
/// executable and neither can be fetched on the user's behalf without showing
/// them the terms first.
///
/// <para>So the app is honest instead: everything that needs a model says so
/// before it is pressed, this screen lists the files with a link each, and the
/// folder they go in is the user's to choose.</para>
/// </remarks>
public sealed partial class ModelsViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// One job at a time, whether it is a read or a change of folder.
    /// </summary>
    /// <remarks>
    /// A flag was not enough. Reads are raised by the window coming to the
    /// front, so one can start at any moment - including the moment the folder
    /// picker closes, which is exactly when the user's own choice arrives.
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeatures))]
    private IReadOnlyList<FeatureCard> _features = [];

    /// <summary>Where the model files are kept.</summary>
    [ObservableProperty]
    private string _folder = string.Empty;

    /// <summary>True while the files are being read and digested.</summary>
    /// <remarks>
    /// Worth showing. Proving the vision graph means reading 1.2 GB, so the
    /// first check after a start is seconds long, and a screen that says "not
    /// installed" until it finishes is telling the user the opposite of the
    /// truth.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    public ModelsViewModel(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    /// <summary>Raised when a feature has become available, or stopped being.</summary>
    /// <remarks>
    /// The side nav and the search box both gate on this, and neither of them
    /// owns it - so the shell is told rather than asked.
    /// </remarks>
    public event EventHandler? AvailabilityChanged;

    public bool HasFeatures => Features.Count > 0;

    public bool HasStatus => Status.Length > 0;

    public bool IsIdle => !IsBusy;

    /// <summary>Whether that feature's models are all present and proved.</summary>
    public bool IsReady(ModelFeature feature) =>
        Features.FirstOrDefault(card => card.Feature == feature)?.IsReady == true;

    /// <summary>
    /// Reads the models folder and reports what is in it.
    /// </summary>
    /// <remarks>
    /// It also takes in anything there that is a model under another name. Two
    /// of the six arrive called <c>model.onnx</c>, so a browser saving both into
    /// one folder produces <c>model.onnx</c> and <c>model (1).onnx</c> - matched
    /// by size and proved by digest, both land under the names this app uses,
    /// and the user is never asked to work out which was which.
    ///
    /// <para>Off the dispatcher, because the first pass digests every file it
    /// finds. Failures are reported rather than thrown: this is started without
    /// being awaited, so an exception would go unobserved and leave the screen
    /// busy for ever.</para>
    /// </remarks>
    public async Task RefreshAsync()
    {
        // Skipped rather than queued, and only this one: a second read of the
        // same folder has the same answer, and this is raised by the window
        // coming to the front, which can happen twice in a second.
        if (!await _gate.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        try
        {
            (IReadOnlyList<FeatureStatus> statuses, string folder) = await Task
                .Run(async () =>
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    GetModelStatusHandler status = scope.ServiceProvider
                        .GetRequiredService<GetModelStatusHandler>();

                    ImportModelsResult sorted = await scope.ServiceProvider
                        .GetRequiredService<ImportModelsHandler>()
                        .HandleAsync(status.Folder)
                        .ConfigureAwait(false);

                    return (sorted.Features, status.Folder);
                })
                .ConfigureAwait(true);

            Folder = folder;
            Apply(statuses);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or InvalidOperationException)
        {
            Status = $"The models folder could not be read: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Keeps the models somewhere else from now on.
    /// </summary>
    /// <remarks>
    /// Nothing is moved or deleted. Whatever is in the old folder stays there,
    /// and if the new one already holds the files the features come straight
    /// back on - which is the point, for a second library that should not mean a
    /// second 1.9 GB.
    /// </remarks>
    public async Task ChooseFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        // Waits, where a read skips. This is the one thing here the user asked
        // for by hand, and dropping it is how it used to fail: closing the
        // folder picker brings the window to the front, the front brings on a
        // re-read, and the choice arrived a moment later to find the screen
        // busy - so nothing happened and nothing said why.
        await _gate.WaitAsync().ConfigureAwait(true);

        IsBusy = true;
        try
        {
            (IReadOnlyList<FeatureStatus> statuses, string chosen) = await Task
                .Run(async () =>
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    GetModelStatusHandler status = scope.ServiceProvider
                        .GetRequiredService<GetModelStatusHandler>();

                    status.UseFolder(folder);

                    ImportModelsResult sorted = await scope.ServiceProvider
                        .GetRequiredService<ImportModelsHandler>()
                        .HandleAsync(status.Folder)
                        .ConfigureAwait(false);

                    return (sorted.Features, status.Folder);
                })
                .ConfigureAwait(true);

            Folder = chosen;
            Apply(statuses);

            // Said out loud, because the screen can otherwise look unchanged: a
            // folder with nothing in it leaves both cards reading exactly what
            // they read before, and the user is left unsure whether the app
            // took the change or ignored it.
            Status = Features.Any(card => card.IsReady)
                ? $"Model files are now read from {chosen}."
                : $"Nothing this app recognises is in {chosen} yet. "
                  + "Downloads saved there from now on will be picked up.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or InvalidOperationException)
        {
            Status = $"That folder could not be used: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Opens the folder, making it if this is the first time.
    /// </summary>
    /// <remarks>
    /// The whole point of naming it is so a download can go straight there, so
    /// it has to be openable before anything is in it.
    /// </remarks>
    [RelayCommand]
    private void OpenFolder() => FolderInExplorer.Open(Folder);

    /// <summary>
    /// Fetches everything a feature is still missing, in one press.
    /// </summary>
    /// <remarks>
    /// One browser tab per file, because these are direct links to the files
    /// themselves and a browser downloads each as it opens it. Whatever is
    /// already installed is left out, so coming back for the one file that
    /// failed does not start 1.2 GB again.
    /// </remarks>
    [RelayCommand]
    private void DownloadFeature(FeatureCard? card)
    {
        if (card is null)
        {
            return;
        }

        foreach (string url in card.Downloads)
        {
            PageInBrowser.Open(url);
        }
    }

    /// <summary>Fetches one file, in the browser Windows is set to use.</summary>
    [RelayCommand]
    private void Download(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            PageInBrowser.Open(url);
        }
    }

    /// <summary>Forgets a message that belongs to the last time this was open.</summary>
    public void Reopened() => Status = string.Empty;

    private void Apply(IReadOnlyList<FeatureStatus> statuses)
    {
        Features = [.. statuses.Select(FeatureCard.Of)];
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
