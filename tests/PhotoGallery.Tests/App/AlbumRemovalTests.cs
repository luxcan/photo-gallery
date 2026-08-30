namespace PhotoGallery.Tests.App;

/// <summary>
/// Removing an album asks first.
/// </summary>
/// <remarks>
/// It did not. "Remove this album" ran the command straight from the markup, and
/// it sits directly under "Save the rule" on the same panel - a very small
/// distance between keeping an album and losing its name, its rule and the work
/// of describing it, none of which come back.
///
/// <para>Asserted as text because none of it is behaviour a view-model test can
/// reach: the question is a modal window raised from the code-behind, and what
/// makes it safe is that the markup no longer offers any other way to the
/// removal.</para>
/// </remarks>
public sealed class AlbumRemovalTests
{
    /// <summary>
    /// The button asks rather than removes.
    /// </summary>
    [Fact]
    public void TheRemoveButtonGoesThroughTheQuestion()
    {
        Assert.Contains("Click=\"OnRemoveAlbumClicked\"", Window(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And nothing else in the markup skips it.
    /// </summary>
    /// <remarks>
    /// The point of the whole change. A second button bound to the command - or
    /// the old binding left behind on this one - removes an album without a word,
    /// and looks exactly like the one that asks.
    /// </remarks>
    [Fact]
    public void NothingInTheMarkupRemovesAnAlbumWithoutAsking()
    {
        Assert.DoesNotContain("Collections.DeleteCommand", Window(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The answer is what runs the removal, and only the answer.
    /// </summary>
    [Fact]
    public void TheRemovalWaitsForTheAnswer()
    {
        string handler = Handler();

        Assert.Contains("AppDialog.Confirm", handler, StringComparison.Ordinal);

        int asked = handler.IndexOf("AppDialog.Confirm", StringComparison.Ordinal);
        int removed = handler.IndexOf("DeleteCommand", StringComparison.Ordinal);

        Assert.True(removed > asked, "The album is removed before the question is asked.");
    }

    /// <summary>
    /// It says what is at stake, which is the photographs.
    /// </summary>
    /// <remarks>
    /// The one thing somebody is actually afraid of when a button says Remove.
    /// The album goes and the pictures do not, and a question that leaves that
    /// unsaid is answered No by people who would have said Yes.
    /// </remarks>
    [Fact]
    public void TheQuestionSaysThePhotographsStay()
    {
        Assert.Contains("stay in your library", Handler(), StringComparison.Ordinal);
    }

    /// <summary>
    /// One question at a time, as everywhere else that asks.
    /// </summary>
    [Fact]
    public void ASecondClickDoesNotStackASecondQuestion()
    {
        Assert.Contains("_confirming", Handler(), StringComparison.Ordinal);
    }

    private static string Window() =>
        File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    /// <summary>The handler alone, so the assertions cannot pass on someone else's.</summary>
    private static string Handler()
    {
        string code = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));

        int start = code.IndexOf("OnRemoveAlbumClicked(", StringComparison.Ordinal);
        Assert.True(start >= 0, "The handler that asks has been renamed or removed.");

        int next = code.IndexOf("\n    private ", start, StringComparison.Ordinal);
        return next < 0 ? code[start..] : code[start..next];
    }
}
