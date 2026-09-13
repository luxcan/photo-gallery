using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoGallery.App.Shell;
using PhotoGallery.Application;

namespace PhotoGallery.App.About;

/// <summary>
/// What the About section knows: where this app came from, and how to get back
/// there. The prose it is read with lives in the view, as every other section's
/// does; what is here is the state that cannot.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>The one address every link on the screen is built from.</summary>
    private const string Repository = "https://github.com/luxcan/photo-gallery";

    private const string Releases = $"{Repository}/releases";

    /// <summary>
    /// What sits under the app's name.
    /// </summary>
    /// <remarks>
    /// The placeholder this used to be said "not yet released", which was true
    /// until the day it stopped being: the project file now states the version,
    /// the build stamps the commit after it, and there is a tag to match. So it
    /// reads the assembly rather than a literal, and can never again be a
    /// sentence about the app that is older than the app.
    /// </remarks>
    public string VersionLine => $"Version {AppVersion.Number}";

    /// <summary>The repository address as it is shown, without the scheme.</summary>
    public string RepositoryLabel => "github.com/luxcan/photo-gallery";

    /// <summary>
    /// What the copy button last did, and null until it is pressed. Said beside
    /// the button rather than in a dialog: copying a link is too small a thing to
    /// interrupt anyone over, and too silent to leave unreported. Null rather
    /// than empty so the line it is read on takes up no room until there is
    /// something to read.
    /// </summary>
    [ObservableProperty]
    private string? _copyNotice;

    [RelayCommand]
    private void OpenReleases() => PageInBrowser.Open(Releases);

    [RelayCommand]
    private void ReportIssue() => PageInBrowser.Open($"{Repository}/issues");

    [RelayCommand]
    private void ViewSource() => PageInBrowser.Open(Repository);

    [RelayCommand]
    private void CopyLink() =>
        CopyNotice = LinkOnClipboard.Copy(Releases)
            ? "The link is on the clipboard."
            : "The clipboard could not be opened — another program may be holding it.";

    /// <summary>
    /// Forgets what the copy button last said.
    /// </summary>
    /// <remarks>
    /// Called when the section is opened, so a notice from an earlier visit is
    /// not still sitting there claiming something was just copied.
    /// </remarks>
    public void Reopened() => CopyNotice = null;
}
