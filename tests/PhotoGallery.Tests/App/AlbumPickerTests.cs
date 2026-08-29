using PhotoGallery.App.Collections;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Finding an album among many, from the photo viewer.
/// </summary>
/// <remarks>
/// The picker has filtered on every keystroke since it was written, and it was
/// still reported as having no search - because the box sat under a list capped
/// at 300 DIP, beside a button marked "Make". Once there were more albums than
/// fit, the only part of it anybody saw was the list, and the way to narrow it
/// was below the fold and labelled as the way to create one.
///
/// <para>So the box moved above the list, and a count says how much of the list
/// is hidden. These tests hold both: the counting, and the order the panel puts
/// its parts in - which is the half that was actually wrong, and the half no
/// view-model test can see.</para>
/// </remarks>
public sealed class AlbumPickerTests
{
    [Fact]
    public void NothingTypedShowsNoCount()
    {
        CollectionPicker picker = Open("Taiwan", "Japan", "Genting");

        Assert.Equal(string.Empty, picker.Narrowed);
        Assert.False(picker.IsNarrowed);
        Assert.Equal(3, picker.Choices.Count);
    }

    [Fact]
    public void TypingSaysHowMuchOfTheListIsHidden()
    {
        CollectionPicker picker = Open("Taiwan", "Japan", "Genting");

        picker.Typed = "an";

        // Taiwan and Genting; Japan has no "an".
        Assert.Equal(2, picker.Choices.Count);
        Assert.Equal("2 of 3", picker.Narrowed);
        Assert.True(picker.IsNarrowed);
    }

    [Fact]
    public void AnEmptyResultStillCountsRatherThanGoingSilent()
    {
        CollectionPicker picker = Open("Taiwan", "Japan");

        picker.Typed = "Peru";

        Assert.Equal("0 of 2", picker.Narrowed);
        Assert.True(picker.HasNoMatch);
    }

    /// <summary>
    /// The box comes before the list, which is the whole of the fix.
    /// </summary>
    /// <remarks>
    /// Asserted on the markup because that is where it lives. Every property
    /// this panel binds was already correct when the search was invisible.
    /// </remarks>
    [Fact]
    public void TheSearchBoxIsAboveTheList()
    {
        string template = PickerTemplate();

        int box = template.IndexOf("<TextBox", StringComparison.Ordinal);
        int list = template.IndexOf("<ScrollViewer", StringComparison.Ordinal);

        Assert.True(box >= 0, "The album picker has no text box at all.");
        Assert.True(list >= 0, "The album picker has no list.");
        Assert.True(box < list, "The album picker's search box is below its list again.");
    }

    [Fact]
    public void TheCountIsShown()
    {
        Assert.Contains("{Binding Narrowed}", PickerTemplate(), StringComparison.Ordinal);
    }

    private static CollectionPicker Open(params string[] names)
    {
        var picker = new CollectionPicker(_ => Task.CompletedTask);

        picker.Open(
            [.. names.Select((name, index) => new CollectionSummary(
                index + 1,
                name,
                DateTime.UnixEpoch,
                DateTime.UnixEpoch,
                CollectionKind.Period,
                CollectionOrigin.Made,
                0,
                null))],
            current: 0,
            prompt: "Which album?",
            hint: "A photograph belongs to one.");

        return picker;
    }

    /// <summary>The CollectionPicker's own slice of Controls.xaml.</summary>
    private static string PickerTemplate()
    {
        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

        int start = controls.IndexOf(
            "DataType=\"{x:Type collections:CollectionPicker}\"", StringComparison.Ordinal);

        Assert.True(start >= 0, "The album picker's template has been renamed or removed.");

        int end = controls.IndexOf("</DataTemplate>", start, StringComparison.Ordinal);
        return controls[start..end];
    }
}
