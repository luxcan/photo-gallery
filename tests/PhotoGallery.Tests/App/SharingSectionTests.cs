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
    [InlineData("Sharing.Offers")]
    [InlineData("Sharing.HasOffers")]
    [InlineData("Sharing.HasUnprepared")]
    [InlineData("Sharing.PicturesLabel")]
    [InlineData("Sharing.TakePicturesCommand")]
    public void EverythingBoundOnTheScreenExists(string path)
    {
        // Bound in the window, and a member of the view model behind it. The
        // second half is what a suite of view-model tests can never check: it
        // would pass just as happily with nothing bound to any of them.
        Assert.Contains(path, s_window);

        string member = path["Sharing.".Length..];

        // ShareCommand is generated from ShareAsync by the toolkit, so the name
        // in the markup is one no source file contains.
        string expected = member switch
        {
            "ShareCommand" => "ShareAsync",
            "TakePicturesCommand" => "TakePicturesAsync",
            _ => member,
        };
        Assert.Contains(expected, s_viewModel);
    }

    [Fact]
    public void TheScreenSaysWhatItIsForBeforeAnyFolderIsChosen()
    {
        // Somebody about to point this at a family drive is entitled to know
        // what it is for first. It sits above the folder picker, so the order is
        // asserted, not just the presence.
        int purpose = s_window.IndexOf(
            "keep each other up to date", StringComparison.Ordinal);
        int picker = s_window.IndexOf("OnChooseSharedFolderClicked", StringComparison.Ordinal);

        Assert.True(purpose > 0, "the screen does not say what sharing is for");
        Assert.True(picker > 0, "the screen has no folder picker");
        Assert.True(purpose < picker, "the purpose is stated after the folder is chosen");
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
    public void PairingIsOfferedAsAQuestionWithAButtonThatAnswersIt()
    {
        // The one step nobody can work out for the user, so it has to be asked
        // on screen rather than assumed anywhere.
        string section = Section();

        Assert.Contains("Is this the same folder?", section);
        Assert.Contains("PairCommand", section);
        Assert.Contains("CommandParameter=\"{Binding}\"", section);

        // Bound through the window, because the button lives inside an item
        // template whose own DataContext is the offer.
        Assert.Contains("RelativeSource={RelativeSource AncestorType=Window}", section);
        Assert.Contains("PairAsync", s_viewModel);
    }

    [Fact]
    public void ThePicturesAreOfferedWithTheirPriceAndOnTheirOwnButton()
    {
        // The first copy is gigabytes and minutes. That is exactly the kind of
        // thing this app says before the click rather than after - and it is a
        // separate press, so a machine can take the answers and decline the
        // gigabytes by simply not making it.
        string section = Section();

        Assert.Contains("Take the small copies", section);
        Assert.Contains("Sharing.PicturesLabel", section);

        int share = section.IndexOf("Sharing.ShareCommand", StringComparison.Ordinal);
        int pictures = section.IndexOf("Sharing.TakePicturesCommand", StringComparison.Ordinal);

        Assert.True(share > 0 && pictures > 0, "one of the two actions is missing");
        Assert.True(share < pictures, "the pictures are offered before the answers are shared");
    }

    [Fact]
    public void TheScreenOffersOnlyTheSharedFolder()
    {
        // The direct connection was removed in favour of the one path this house
        // actually has. What must not come back by accident is half of it: an
        // address box, a pairing code, or a warning about a network profile that
        // now stops nothing.
        string section = Section();

        foreach (string gone in new[] { "TypedAddress", "TypedCode", "MyCode", "NetworkProblem" })
        {
            Assert.DoesNotContain(gone, section, StringComparison.Ordinal);
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

    /// <summary>
    /// The notice that floats over every screen keeps the same vocabulary.
    /// </summary>
    /// <remarks>
    /// It lives outside the Sharing screen in the markup, because it follows the
    /// reader around rather than waiting on one page - so the slice that polices
    /// the screen's words does not reach it. That makes it the one piece of
    /// sharing copy most people in the house will ever read, and the last place
    /// that should start calling the other laptop a peer.
    ///
    /// <para>Both halves of it: the words in the markup, and the sentence the
    /// view model builds and hands to it.</para>
    /// </remarks>
    [Fact]
    public void TheNoticeOverTheAppNamesNoDeviceAndNoProtocol()
    {
        string notice = Notice();

        foreach (Match text in Regex.Matches(notice, "(?:Text|Content|ToolTip)=\"([^\"]*)\""))
        {
            foreach (string banned in Banned)
            {
                Assert.DoesNotContain(
                    banned, text.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The sentence itself is built in the view model, and pinned by what it
        // says rather than by what it does not. Hunting banned words through C#
        // source finds "sync " inside every "async Task" in the file, which is
        // how a negative test teaches people that it cries wolf.
        Assert.Contains("this computer has not taken yet", s_viewModel, StringComparison.Ordinal);
    }

    /// <summary>The words this feature never uses for the machines in a house.</summary>
    private static readonly string[] Banned =
        ["NAS", "peer", "server", "protocol", "sync "];

    /// <summary>
    /// The floating notice, from the flag that shows it to the button that puts
    /// it down.
    /// </summary>
    private static string Notice()
    {
        int start = s_window.IndexOf("Sharing.HasNotice", StringComparison.Ordinal);
        Assert.True(start > 0, "the notice over the app is not in the window");

        int end = s_window.IndexOf(
            "Sharing.DismissNoticeCommand", start, StringComparison.Ordinal);
        Assert.True(end > start, "the notice no longer carries the way to put it down");

        return s_window[start..end];
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
