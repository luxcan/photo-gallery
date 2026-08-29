using System.Text.RegularExpressions;

namespace PhotoGallery.Tests.App;

/// <summary>
/// That the Sharing screen is actually in the window, wired to the view model
/// that drives it.
/// </summary>
/// <remarks>
/// A binding to a property that does not exist fails silently in WPF: the
/// control draws empty and the app carries on. A view-model suite cannot see any
/// of it - every property here can be perfect while nothing on screen is bound
/// to one. So these read the markup as text, which is the only place the wiring
/// is written down.
/// </remarks>
public sealed class SharingSectionTests
{
    private static readonly string s_window =
        File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    private static readonly string s_viewModel =
        File.ReadAllText(AppMarkup.PathTo("Sharing", "SharingViewModel.cs"));

    [Fact]
    public void TheSectionIsInTheWindowAndGatedOnItsOwnFlag()
    {
        Assert.Contains("ShowSharing, Converter={StaticResource BoolToVisibility}", s_window);
    }

    [Theory]
    [InlineData("Sharing.Folder")]
    [InlineData("Sharing.Problem")]
    [InlineData("Sharing.Status")]
    [InlineData("Sharing.WaitingLabel")]
    [InlineData("Sharing.Machines")]
    [InlineData("Sharing.HasFolder")]
    [InlineData("Sharing.HasProblem")]
    [InlineData("Sharing.HasStatus")]
    [InlineData("Sharing.HasWaiting")]
    [InlineData("Sharing.HasMachines")]
    [InlineData("Sharing.IsIdle")]
    [InlineData("Sharing.ShareCommand")]
    public void EverythingBoundOnTheScreenExists(string path)
    {
        // Bound in the window, and a member of the view model behind it. The
        // second half is what a suite of view-model tests can never check: it
        // would pass just as happily with nothing bound to any of them.
        Assert.Contains(path, s_window);

        string member = path["Sharing.".Length..];

        // ShareCommand is generated from ShareAsync by the toolkit, so the name
        // in the markup is one no source file contains.
        string expected = member == "ShareCommand" ? "ShareAsync" : member;
        Assert.Contains(expected, s_viewModel);
    }

    [Fact]
    public void TheScreenSaysWhatWillNotBeSentBeforeAnyFolderIsChosen()
    {
        // A rule of the design, not a nicety: somebody about to point this at a
        // family drive is entitled to know the photographs stay where they are.
        // It sits above the folder picker, so the order is asserted, not just
        // the presence.
        int promise = s_window.IndexOf(
            "Your photographs are never sent", StringComparison.Ordinal);
        int picker = s_window.IndexOf("OnChooseSharedFolderClicked", StringComparison.Ordinal);

        Assert.True(promise > 0, "the screen does not say that originals never travel");
        Assert.True(picker > 0, "the screen has no folder picker");
        Assert.True(promise < picker, "the promise is made after the folder is chosen");
    }

    [Fact]
    public void ThereIsOneButtonToShareAndNotTwo()
    {
        // Nobody wants to publish. Separate send and receive would be a
        // procedure to remember, whose wrong order is not an error anything
        // could report.
        string section = Section();

        Assert.Contains("Share now", section);
        Assert.DoesNotContain("Content=\"Publish", section);
        Assert.DoesNotContain("Content=\"Send", section);
        Assert.DoesNotContain("Content=\"Receive", section);
    }

    [Fact]
    public void TheScreenNamesNoDeviceAndNoProtocol()
    {
        // The app's copy rules: "the other computers in the house", not "the
        // NAS", not "the peer", not "sync".
        foreach (Match text in Regex.Matches(Section(), "Text=\"([^\"]*)\""))
        {
            string copy = text.Groups[1].Value;

            foreach (string banned in new[] { "NAS", "peer", "server", "protocol", "sync " })
            {
                Assert.DoesNotContain(banned, copy, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ThePickerHandlerExists()
    {
        // A Click in markup that names no method compiles, and throws when
        // pressed.
        string codeBehind = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));

        Assert.Contains("Click=\"OnChooseSharedFolderClicked\"", s_window);
        Assert.Contains("private async void OnChooseSharedFolderClicked", codeBehind);
    }

    /// <summary>Just the Sharing screen's markup, so the assertions are about it.</summary>
    private static string Section()
    {
        int start = s_window.IndexOf(
            "<Grid Visibility=\"{Binding ShowSharing", StringComparison.Ordinal);
        Assert.True(start > 0, "the Sharing section is not in the window");

        int end = s_window.IndexOf(
            "<Grid Visibility=\"{Binding ShowAbout", StringComparison.Ordinal);
        Assert.True(end > start, "the Sharing section does not end where it should");

        return s_window[start..end];
    }
}
