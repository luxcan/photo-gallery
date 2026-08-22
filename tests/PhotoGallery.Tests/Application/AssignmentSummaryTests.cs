using PhotoGallery.Application.UseCases.People;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The sentence the People screen shows after an answer has been recorded.
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
    [InlineData(1, 0, "1 face is Ana")]
    [InlineData(16, 0, "16 faces are Ana")]
    [InlineData(1, 1, "1 face is Ana, and 1 more looks like them")]
    [InlineData(16, 7, "16 faces are Ana, and 7 more look like them")]
    public void Summary_SpellsBothOfItsCounts(int assigned, int proposed, string expected) =>
        Assert.Equal(expected, Result(assigned, proposed, matched: proposed).Summary);

    [Fact]
    public void Summary_SaysSoWhenThereWereMoreThanItIsShowing()
    {
        // Three people all showing exactly three hundred is a cap being met, and
        // a number that stops at a round figure without saying so reads as the
        // whole answer.
        AssignmentResult result = Result(assigned: 4, proposed: 300, matched: 812);

        Assert.True(result.WasCapped);
        Assert.Equal(
            "4 faces are Ana. 812 more look like them - showing the closest 300, "
            + "and the rest after you answer these.",
            result.Summary);
    }

    [Fact]
    public void Summary_OfAnAnswerThatOpenedNoNewQuestionsStopsThere()
    {
        // Nothing more to say, and "and 0 more look like them" would read as a
        // failure to find something.
        Assert.Equal("2 faces are Ana", Result(assigned: 2, proposed: 0, matched: 0).Summary);
    }

    private static AssignmentResult Result(int assigned, int proposed, int matched) =>
        new(
            PersonId: 1,
            DisplayName: "Ana",
            Assigned: assigned,
            Eras: 1,
            Proposed: proposed,
            Matched: matched,
            Considered: 1_000);
}
