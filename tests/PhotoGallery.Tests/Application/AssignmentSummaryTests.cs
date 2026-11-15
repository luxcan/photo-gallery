using PhotoGallery.Application.UseCases.People;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The sentence the People screen shows after an answer has been recorded, and
/// the line the log gets once the round it asked for is over.
/// </summary>
/// <remarks>
/// It is the only confirmation the user gets that the button did anything, so it
/// has to read as a sentence somebody wrote. Answering the last question about
/// one person - the most likely of all of these to be read - said "1 faces are
/// Vera".
/// </remarks>
public sealed class AssignmentSummaryTests
{
    [Theory]
    [InlineData(1, false, "1 face is Ana")]
    [InlineData(16, false, "16 faces are Ana")]
    [InlineData(1, true, "1 face is not Ana")]
    [InlineData(16, true, "16 faces are not Ana")]
    public void Summary_SaysOnlyWhatWasDecided(int assigned, bool rejected, string expected) =>
        Assert.Equal(
            expected,
            new AssignmentResult(PersonId: 1, DisplayName: "Ana", assigned, Eras: 1, rejected).Summary);

    [Fact]
    public void RoundSummary_SaysSoWhenThereWereMoreThanItIsShowing()
    {
        // Three people all showing exactly three hundred is a cap being met, and
        // a number that stops at a round figure without saying so reads as the
        // whole answer. Said by the round now, because the answer no longer
        // waits for one.
        ProposalRound round = Round(offered: 300, matched: 812);

        Assert.True(round.WasCapped);
        Assert.Equal(
            "suggestions updated; 812 faces look like somebody, showing the closest 300",
            round.Summary);
    }

    [Theory]
    [InlineData(0, "suggestions updated; nothing new to look at")]
    [InlineData(1, "suggestions updated; 1 face to look at")]
    [InlineData(7, "suggestions updated; 7 faces to look at")]
    public void RoundSummary_CountsWhatIsWaiting(int offered, string expected) =>
        Assert.Equal(expected, Round(offered, matched: offered).Summary);

    private static ProposalRound Round(int offered, int matched) =>
        new(offered, matched, Considered: 1_000, new Dictionary<int, PersonProposals>());
}
