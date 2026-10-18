using System.Text.RegularExpressions;
using PhotoGallery.App.Sharing;
using PhotoGallery.Application.UseCases.Sharing;

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
    [InlineData("Sharing.HasWaitingElsewhere")]
    [InlineData("Sharing.WaitingElsewhereLabel")]
    [InlineData("Sharing.HasWaitingOnPhotos")]
    [InlineData("Sharing.WaitingOnPhotosLabel")]
    [InlineData("Sharing.HasWaitingOnFaces")]
    [InlineData("Sharing.WaitingOnFacesLabel")]
    [InlineData("Sharing.HasWaitingHere")]
    [InlineData("Sharing.WaitingHereLabel")]
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

        Assert.Contains("Sync now", section);
        Assert.DoesNotContain("Content=\"Publish", section);
        Assert.DoesNotContain("Content=\"Send", section);
        Assert.DoesNotContain("Content=\"Receive", section);
    }

    /// <summary>
    /// Only the group somebody can act on is allowed to shout.
    /// </summary>
    /// <remarks>
    /// Two of the three waiting groups are nobody's to fix: a photograph that
    /// has been renamed where it lives, and a face this machine's detector will
    /// never draw. Dressing those in the caution colours is how a screen teaches
    /// people that its warnings mean nothing - which is exactly what the single
    /// red banner did, every time, for ever. So the weight is asserted and not
    /// only the wording, because "make it consistent" is a one-line edit that
    /// would put it straight back.
    /// </remarks>
    [Fact]
    public void OnlyTheGroupSomebodyCanActOnGetsACallout()
    {
        string waiting = Waiting();

        Assert.Equal(1, Occurrences(waiting, "Status.CautionBack"));

        int caution = waiting.IndexOf("Status.CautionBack", StringComparison.Ordinal);
        int actionable = waiting.IndexOf(
            "Sharing.WaitingElsewhereLabel", StringComparison.Ordinal);
        int closes = waiting.IndexOf("</Border>", caution, StringComparison.Ordinal);

        Assert.True(caution < actionable, "the callout does not wrap the actionable line");
        Assert.True(actionable < closes, "the actionable line is outside the callout");

        // And the others are hints rather than anything louder.
        foreach (string quiet in new[]
            { "WaitingOnPhotosLabel", "WaitingOnFacesLabel", "WaitingHereLabel" })
        {
            int at = waiting.IndexOf(quiet, StringComparison.Ordinal);
            int style = waiting.LastIndexOf("Style=\"{DynamicResource", at, StringComparison.Ordinal);

            Assert.Contains(
                "HintText",
                waiting[style..at],
                StringComparison.Ordinal);
        }
    }

    /// <summary>The one worth doing something about is offered first.</summary>
    [Fact]
    public void TheWaitingGroupsAreOrderedByWhatCanBeDoneAboutThem()
    {
        string waiting = Waiting();

        int elsewhere = waiting.IndexOf("WaitingElsewhereLabel", StringComparison.Ordinal);
        int photos = waiting.IndexOf("WaitingOnPhotosLabel", StringComparison.Ordinal);
        int faces = waiting.IndexOf("WaitingOnFacesLabel", StringComparison.Ordinal);
        int here = waiting.IndexOf("WaitingHereLabel", StringComparison.Ordinal);

        Assert.True(elsewhere > 0 && photos > elsewhere && faces > photos && here > faces);
    }

    [Fact]
    public void TheScreenNamesNoDeviceAndNoProtocol()
    {
        // The app's copy rules: "the other computers in the house", not "the
        // NAS", not "the peer".
        foreach (Match text in Regex.Matches(Section(), "Text=\"([^\"]*)\""))
        {
            string copy = text.Groups[1].Value;

            foreach (string banned in Banned)
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

    /// <summary>
    /// The small copies have no button of their own: a scan takes and leaves
    /// them.
    /// </summary>
    /// <remarks>
    /// The button showed only on a laptop that still had photographs to prepare,
    /// and said "take" - so a laptop with everything prepared never gave, and
    /// the folder never held a picture. A button that has to be remembered after
    /// every scan is the procedure this app does not ask anybody to follow.
    /// </remarks>
    [Fact]
    public void TheSmallCopiesHaveNoButtonOfTheirOwn()
    {
        string section = Section();

        Assert.DoesNotContain("TakePicturesCommand", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Sharing.HasUnprepared", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Take the small copies", section, StringComparison.Ordinal);
        Assert.DoesNotContain("TakePicturesAsync", s_viewModel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The screen says where the small copies come from and go to now, and what
    /// they cost the folder chosen above.
    /// </summary>
    /// <remarks>
    /// A folder a cloud service keeps in step is one of the two kinds this
    /// screen suggests, and a scan puts gigabytes in it. That is said before
    /// anybody scans rather than discovered in a bill.
    /// </remarks>
    [Fact]
    public void TheScreenSaysAScanTakesAndLeavesTheSmallCopiesAndWhatThatCosts()
    {
        string section = Section();

        Assert.Contains("Each scan takes the small copies", section, StringComparison.Ordinal);
        Assert.Contains("leaves its own there", section, StringComparison.Ordinal);
        Assert.Contains("a few gigabytes the first time", section, StringComparison.Ordinal);
        Assert.Contains("never copied", section, StringComparison.Ordinal);
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

        // The sentence itself is built in the view model, so it is asked for
        // there, in every arrangement of computers it can be about, and read as
        // the sentence somebody will see. A negative test over the C# source
        // would be the weaker instrument: it reads every doc-comment and every
        // identifier as though they were copy, and cries wolf on both.
        foreach (string sentence in EveryNotice())
        {
            Assert.Contains("has not taken yet.", sentence, StringComparison.Ordinal);
            Assert.True(char.IsUpper(sentence[0]), $"the notice begins in lower case: {sentence}");

            foreach (string banned in Banned)
            {
                Assert.DoesNotContain(banned, sentence, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// The sentence about answers waiting, in every arrangement of computers it
    /// can be about: named, never taken from, and both, one and several.
    /// </summary>
    private static IEnumerable<string> EveryNotice()
    {
        DateTime now = DateTime.UtcNow;

        return
        [
            SharingViewModel.Asking(new SharedUpdate(["Mum's laptop"], 0, now)),
            SharingViewModel.Asking(new SharedUpdate([], 1, now)),
            SharingViewModel.Asking(new SharedUpdate(["Dad's laptop", "Mum's laptop"], 0, now)),
            SharingViewModel.Asking(new SharedUpdate(["Mum's laptop"], 1, now)),
            SharingViewModel.Asking(new SharedUpdate(["Mum's laptop"], 2, now)),
            SharingViewModel.Asking(new SharedUpdate([], 2, now)),
        ];
    }

    /// <summary>The words this feature never uses for the machines in a house.</summary>
    /// <remarks>
    /// "sync" was on this list until the feature was named Sync. It was banned
    /// alongside the others because it promises something continuous and
    /// automatic, and this is a button somebody presses - so the name is allowed
    /// now and the promise still is not: no copy here may say the app keeps
    /// anything up to date on its own. The rest stay. A machine in this house is
    /// "the other computers", never a device and never a mechanism.
    /// </remarks>
    private static readonly string[] Banned =
        ["NAS", "peer", "server", "protocol"];

    /// <summary>
    /// The floating notice, from the flag that shows it to the button that puts
    /// it down.
    /// </summary>
    /// <remarks>
    /// The panel belongs to the shell rather than to this screen now, because a
    /// newer version of the app speaks through it too - but the sentence about
    /// another computer's answers is still sharing's, and still has to keep
    /// sharing's vocabulary, which is why the rule is still asserted from here.
    /// </remarks>
    private static string Notice()
    {
        int start = s_window.IndexOf(
            "Visibility=\"{Binding HasNotice", StringComparison.Ordinal);
        Assert.True(start > 0, "the notice over the app is not in the window");

        int end = s_window.IndexOf(
            "DismissNoticeCommand", start, StringComparison.Ordinal);
        Assert.True(end > start, "the notice no longer carries the way to put it down");

        return s_window[start..end];
    }

    /// <summary>Just the waiting-answers block, so its weight can be read.</summary>
    private static string Waiting()
    {
        string section = Section();

        int start = section.IndexOf(
            "Answers waiting for their photos", StringComparison.Ordinal);
        Assert.True(start > 0, "the waiting answers are not on the screen");

        int end = section.IndexOf(
            "Small copies of your photos", start, StringComparison.Ordinal);
        Assert.True(end > start, "the waiting answers do not end where they should");

        return section[start..end];
    }

    private static int Occurrences(string text, string what)
    {
        int count = 0;
        for (int at = text.IndexOf(what, StringComparison.Ordinal);
             at >= 0;
             at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>Just the Sync screen's markup, so the assertions are about it.</summary>
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
