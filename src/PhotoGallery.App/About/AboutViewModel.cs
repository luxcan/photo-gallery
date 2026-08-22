using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoGallery.App.Shell;

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
    /// A placeholder, deliberately. Nothing stamps a version into this build:
    /// the assembly carries .NET's default 1.0.0, which on a screen would read
    /// as a first release that has not happened, and there is no tag to take a
    /// build date from either. When the app is published from a tag, replace
    /// this with <c>AssemblyInformationalVersion</c> and that tag's date.
    /// </remarks>
    public string VersionLine => "Version 0.1.0 · not yet released";

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
