namespace PhotoGallery.Tests.App;

/// <summary>
/// Which failures the work that writes to the library agrees to report.
/// </summary>
/// <remarks>
/// A catch filter is not behaviour a view-model test can reach: the fixtures
/// that drive these screens hand them fakes, and a fake that throws what a real
/// database throws proves only that the fake was told to. So this reads the
/// source, the way the tests that pin a binding read the markup.
///
/// <para>The rule it pins was learned the expensive way. A scan on the second
/// machine in this house raised a DbUpdateConcurrencyException - a write whose
/// row had gone - from somewhere inside an eight-phase pass. Every one of those
/// phases writes to the library, but the filter guarding the whole pass named
/// three file exceptions and nothing else, so the fault went past it, past the
/// command, and into the handler that exists for faults meaning the app itself
/// is wrong: a message box quoting Entity Framework at somebody who had been
/// waiting an hour, and then the app closed with the scan's work abandoned.
/// LibraryFailure had covered that exception all along.</para>
///
/// <para>This is deliberately narrow. LibraryFailure's own remark says a filter
/// naming only file exceptions is a claim about that one place rather than a
/// mistake, so this asks the question only of the two surfaces that are known to
/// write: the scan, and the screen that merges another computer's answers.</para>
/// </remarks>
public sealed class LibraryFailureFilterTests
{
    [Fact]
    public void TheScanAsksLibraryFailureRatherThanNamingFileFaults()
    {
        string scan = MethodIn("ViewModels", "MainViewModel.cs", "private async Task RunRefreshAsync");

        Assert.Contains("LibraryFailure.IsExpected(ex)", scan, StringComparison.Ordinal);
        Assert.DoesNotContain("when (ex is IOException", scan, StringComparison.Ordinal);
    }

    [Fact]
    public void SoDoesTheRunThatLooksForPeople()
    {
        string people = MethodIn("ViewModels", "MainViewModel.cs", "private async Task RecheckPeopleAsync");

        Assert.Contains("LibraryFailure.IsExpected(ex)", people, StringComparison.Ordinal);
        Assert.DoesNotContain("when (ex is IOException", people, StringComparison.Ordinal);
    }

    /// <summary>
    /// And every filter on the sharing screen, which runs the same merge the
    /// scan runs and had the same gap in all four of its.
    /// </summary>
    [Fact]
    public void TheSharingScreenNamesNoFaultOfItsOwn()
    {
        string sharing = File.ReadAllText(AppMarkup.PathTo("Sharing", "SharingViewModel.cs"));

        Assert.DoesNotContain("when (ex is IOException", sharing, StringComparison.Ordinal);

        // Every catch on the screen, not a fixed number of them: the count is
        // the thing most likely to change for an honest reason, and a test that
        // has to be edited whenever the screen grows a method is a test people
        // edit without reading.
        int guarded = Count(sharing, "catch (Exception ex) when (");
        int asking = Count(sharing, "catch (Exception ex) when (LibraryFailure.IsExpected(ex))");

        Assert.Equal(guarded, asking);
        Assert.True(guarded >= 4, $"The sharing screen should still be guarding its work; found {guarded}.");
    }

    /// <summary>
    /// One method's text, from its signature to the start of the next member.
    /// </summary>
    private static string MethodIn(string folder, string file, string signature)
    {
        string source = File.ReadAllText(AppMarkup.PathTo(folder, file));

        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, $"{file} no longer declares {signature}.");

        // The closing brace of a member at class level, which is the one thing
        // in this file that sits at exactly four spaces.
        int end = source.IndexOf("\r\n    }\r\n", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{signature} in {file} does not appear to end.");

        return source[start..end];
    }

    private static int Count(string source, string text)
    {
        int found = 0;

        for (int at = source.IndexOf(text, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}
