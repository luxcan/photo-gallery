using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Sharing;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What the Sync screen says about answers waiting for their photographs.
/// </summary>
/// <remarks>
/// There was one sentence for all of them: "N answers are waiting for photos
/// this library has not indexed yet. Scanning will bring them in." On a real
/// library it was false for every one of the thirty-eight it counted, and it had
/// no test on a single word of it - while the sentence next to it, about the
/// small copies, had a suite pinning its phrasing exactly. This is that suite for
/// this sentence.
///
/// <para>What is pinned is chiefly what the copy must never do again: promise a
/// remedy that cannot work. Two of the three groups are nobody's to fix, and
/// saying otherwise is what made the old line worse than silence.</para>
/// </remarks>
public sealed class WaitingCopyTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly SharingViewModel _sharing;

    public WaitingCopyTests() =>
        _sharing = new SharingViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(), new SilentLog());

    private sealed class SilentLog : IActivityLog
    {
        public void Append(string line)
        {
        }
    }

    /// <summary>
    /// The face line offers a scan as a possibility, never as an instruction.
    /// </summary>
    /// <remarks>
    /// Some of these come good on the next scan and some never do, and which is
    /// which cannot be known from here - so the sentence says "may" and never
    /// promises that a scan will bring them in.
    /// </remarks>
    [Fact]
    public void TheFacesLineOffersTheNextScanOnlyAsAPossibility()
    {
        _sharing.WaitingOnFaces = 4;

        Assert.Contains(
            "4 answers are waiting on faces this computer has not found yet",
            _sharing.WaitingOnFacesLabel,
            StringComparison.Ordinal);

        Assert.Contains(
            "The next scan may pick them up",
            _sharing.WaitingOnFacesLabel,
            StringComparison.Ordinal);
        Assert.DoesNotContain("will", _sharing.WaitingOnFacesLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The line about photographs that are already here says nothing about faces.
    /// </summary>
    /// <remarks>
    /// A turn waiting for a small copy and a move waiting for an album used to be
    /// counted under the face line, which told whoever read it that a scan's
    /// face pass might bring them in. It never would, so this line names what
    /// they are waiting for instead.
    /// </remarks>
    [Fact]
    public void TheLineAboutPhotosAlreadyHereSaysWhatTheyWaitForAndNotAFace()
    {
        _sharing.WaitingHere = 2;

        string said = _sharing.WaitingHereLabel;

        Assert.Contains(
            "2 answers about photos that are already here are waiting",
            said,
            StringComparison.Ordinal);
        Assert.Contains("small copies to turn", said, StringComparison.Ordinal);
        Assert.Contains("albums to put them in", said, StringComparison.Ordinal);
        Assert.DoesNotContain("face", said, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The line about photographs that are already here allows for a scan that
    /// stopped before its answers landed.
    /// </summary>
    /// <remarks>
    /// Answers land in a scan's last phase, so a scan stopped earlier - or one
    /// whose earlier phase failed - leaves every answer about a photograph it has
    /// just indexed waiting for the next one. A move into an album this computer
    /// has, a rejection, a turn whose small copy was just made: all of them sit
    /// in this group then, and the line once told whoever read it that this
    /// computer did not have what they needed. It did; they needed a scan to
    /// finish. So the line names that first, and offers the next scan only as
    /// a possibility, as the face line does.
    /// </remarks>
    [Fact]
    public void TheLineAboutPhotosAlreadyHereAllowsForAScanThatStoppedBeforeTheyLanded()
    {
        _sharing.WaitingHere = 1;

        string said = _sharing.WaitingHereLabel;

        Assert.Contains("for the end of a scan", said, StringComparison.Ordinal);
        Assert.Contains("the next scan may pick it up", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("does not have", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The missing line is conditional too, and says the photos must come back
    /// under their old names.
    /// </summary>
    [Fact]
    public void TheMissingLineIsConditionalOnThePhotosTurningUpUnderTheirOldNames()
    {
        _sharing.WaitingOnPhotos = 37;

        Assert.Contains(
            "37 answers are waiting for photos that are not in your folders",
            _sharing.WaitingOnPhotosLabel,
            StringComparison.Ordinal);

        Assert.Contains("If they turn up", _sharing.WaitingOnPhotosLabel, StringComparison.Ordinal);
        Assert.Contains(
            "under their old names, a scan brings the answers in",
            _sharing.WaitingOnPhotosLabel,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The one actionable line names the folder and gives one instruction.
    /// </summary>
    /// <remarks>
    /// Counted in photographs, because a folder of photographs is the thing
    /// being asked for.
    /// </remarks>
    [Fact]
    public void TheElsewhereLineNamesTheFolderAndSaysWhereToPutIt()
    {
        _sharing.WaitingElsewhere = 812;
        _sharing.WaitingElsewherePhotos = 812;
        _sharing.ElsewhereFolder = "2026 Phone Dump";

        string said = _sharing.WaitingElsewhereLabel;

        Assert.Contains("812 photos this computer cannot see", said, StringComparison.Ordinal);
        Assert.Contains("2026 Phone Dump", said, StringComparison.Ordinal);
        Assert.Contains(
            "Put that folder where every computer can reach it.",
            said,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And still reads as an instruction when there is no one folder to name.
    /// </summary>
    [Fact]
    public void TheElsewhereLineStillWorksWithNoFolderToName()
    {
        _sharing.WaitingElsewhere = 5;
        _sharing.WaitingElsewherePhotos = 5;
        _sharing.ElsewhereFolder = string.Empty;

        string said = _sharing.WaitingElsewhereLabel;

        Assert.DoesNotContain("called \"\"", said, StringComparison.Ordinal);
        Assert.Contains("a folder only another computer has", said, StringComparison.Ordinal);
        Assert.Contains("Put that folder", said, StringComparison.Ordinal);
    }

    /// <summary>One is said as one, in all four.</summary>
    [Fact]
    public void OneIsSaidAsOne()
    {
        _sharing.WaitingOnFaces = 1;
        _sharing.WaitingHere = 1;
        _sharing.WaitingOnPhotos = 1;
        _sharing.WaitingElsewhere = 1;
        _sharing.WaitingElsewherePhotos = 1;

        Assert.Contains(
            "1 answer is waiting on a face", _sharing.WaitingOnFacesLabel, StringComparison.Ordinal);
        Assert.Contains(
            "1 answer about a photo that is already here is waiting",
            _sharing.WaitingHereLabel,
            StringComparison.Ordinal);
        Assert.Contains(
            "1 answer is waiting for a photo",
            _sharing.WaitingOnPhotosLabel,
            StringComparison.Ordinal);
        Assert.Contains(
            "1 photo this computer cannot see",
            _sharing.WaitingElsewhereLabel,
            StringComparison.Ordinal);

        foreach (string said in All())
        {
            Assert.DoesNotContain("1 answers", said, StringComparison.Ordinal);
            Assert.DoesNotContain("1 photos", said, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// None of them tells anybody that scanning will fix it.
    /// </summary>
    /// <remarks>
    /// The whole point. The old line promised it about every held answer, and
    /// the promise was the part that was wrong - so it is the part asserted, in
    /// the words it used.
    /// </remarks>
    [Fact]
    public void NoneOfThemPromisesThatScanningWillBringThemIn()
    {
        _sharing.WaitingOnFaces = 3;
        _sharing.WaitingHere = 3;
        _sharing.WaitingOnPhotos = 3;
        _sharing.WaitingElsewhere = 3;
        _sharing.WaitingElsewherePhotos = 3;

        foreach (string said in All())
        {
            Assert.DoesNotContain("will bring", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("has not indexed", said, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The screen keeps quiet when there is nothing waiting.</summary>
    [Fact]
    public void NothingWaitingShowsNothing()
    {
        Assert.False(_sharing.HasWaiting);
        Assert.False(_sharing.HasWaitingOnFaces);
        Assert.False(_sharing.HasWaitingHere);
        Assert.False(_sharing.HasWaitingOnPhotos);
        Assert.False(_sharing.HasWaitingElsewhere);
    }

    /// <summary>Any one group on its own is enough to show the block.</summary>
    [Fact]
    public void AnyOneGroupOpensTheBlock()
    {
        _sharing.WaitingOnFaces = 1;
        Assert.True(_sharing.HasWaiting);

        _sharing.WaitingOnFaces = 0;
        _sharing.WaitingHere = 1;
        Assert.True(_sharing.HasWaiting);

        _sharing.WaitingHere = 0;
        _sharing.WaitingOnPhotos = 1;
        Assert.True(_sharing.HasWaiting);

        _sharing.WaitingOnPhotos = 0;
        _sharing.WaitingElsewhere = 1;
        Assert.True(_sharing.HasWaiting);
    }

    private string[] All() =>
    [
        _sharing.WaitingOnFacesLabel,
        _sharing.WaitingHereLabel,
        _sharing.WaitingOnPhotosLabel,
        _sharing.WaitingElsewhereLabel,
    ];

    public void Dispose() => _services.Dispose();
}
