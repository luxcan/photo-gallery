using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// The sentence a sync leaves on screen, in the words the user chose for it.
/// </summary>
/// <remarks>
/// It used to say "Nothing new. Your answers are synced." - two facts that read
/// as unrelated, in a word ("answers") that means nothing to anybody outside
/// the code.
/// </remarks>
public sealed class SyncSummaryWordingTests
{
    private static readonly PublishResult Sent = new(true, string.Empty, 0, 0, 0, DateTime.UtcNow);

    private static readonly PublishResult NotSent = PublishResult.CouldNot("the folder went away");

    [Fact]
    public void NothingCameIn_SaysOnlyThatItIsUpToDate()
    {
        Assert.Equal("You're up to date.", new ShareResult(Merge(), Sent).Summary);
    }

    [Fact]
    public void SomethingCameIn_SaysWhatAndThatItIsUpToDate()
    {
        Assert.Equal(
            "5 photos moved. You're up to date.",
            new ShareResult(Merge(moved: 5), Sent).Summary);
    }

    [Fact]
    public void ChangesCouldNotBeSent_SaysSoAfterWhatCameIn()
    {
        Assert.Equal(
            "5 photos moved, but you are unable to sync your changes with others.",
            new ShareResult(Merge(moved: 5), NotSent).Summary);
    }

    [Fact]
    public void NothingCameInAndChangesCouldNotBeSent_SaysOnlyThat()
    {
        Assert.Equal(
            "You are unable to sync your changes with others.",
            new ShareResult(Merge(), NotSent).Summary);
    }

    /// <summary>
    /// A computer that could not be read was not heard from, so the library is
    /// not up to date and must not say so.
    /// </summary>
    [Fact]
    public void AComputerCouldNotBeRead_IsNotCalledUpToDate()
    {
        MergeResult merged = Merge(moved: 5) with
        {
            Unreadable = [new UnreadableAnswers("Dad's laptop", "half written")],
        };

        Assert.Equal("5 photos moved.", new ShareResult(merged, Sent).Summary);
    }

    [Fact]
    public void NoOtherComputerHasSynced_IsNotCalledUpToDate()
    {
        MergeResult merged = Merge() with { Machines = 0 };

        Assert.DoesNotContain("up to date", new ShareResult(merged, Sent).Summary, StringComparison.Ordinal);
    }

    private static MergeResult Merge(int moved = 0) =>
        new(
            true,
            string.Empty,
            MergeOutcome.Nothing with { PhotographsMoved = moved },
            Machines: 1,
            Unreadable: [],
            Pairings: []);
}
