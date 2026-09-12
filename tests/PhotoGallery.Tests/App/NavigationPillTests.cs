using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// How the side nav says which section is open, and how it tells its two bands
/// apart.
/// </summary>
/// <remarks>
/// The open section used to be a tinted row with a 2 DIP accent rail down its
/// left edge. It is now the whole row filled with the accent and white text on
/// it, and the rail is gone. Three separate things have to agree for that to
/// look right, and none of them is checked by anything else: the fill has to be
/// the accent rather than the list tint - pointing the tint at the accent
/// instead would take the folder tree and every combo box with it, and put
/// near-black text on blue - the foreground has to flip to the accent's own, and
/// the keyboard focus has to land somewhere now that the rail it used to colour
/// no longer exists.
///
/// <para>The two bands are the other half. The sections are drawn at full
/// strength and the three at the foot a step back, both from one template. That
/// was first written as a colour set on each band and inherited by the rows,
/// which does not work and does not say so: a ToggleButton keeps WPF's own
/// default style for its type alongside an explicit one, and a theme-style
/// setter outranks inheritance, so every unselected row came out in system
/// black - invisible on the dark pane, and indistinguishable from correct on
/// the light one. Each band now names its own colour on its own style, and the
/// tests below hold that rather than the mechanism that failed.</para>
/// </remarks>
public sealed class NavigationPillTests
{
    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheOpenSectionIsFilledWithTheAccent()
    {
        XElement open = CheckedTrigger();

        Assert.Equal(
            "{DynamicResource Button.Background}",
            (string?)Setter(open, "Fill", "Background")?.Attribute("Value"));
        Assert.Equal(
            "{DynamicResource Button.Foreground}",
            (string?)Setter(open, null, "Foreground")?.Attribute("Value"));
    }

    [Fact]
    public void TheRailIsGone()
    {
        Assert.DoesNotContain("x:Name=\"Indicator\"", Controls(), StringComparison.Ordinal);
    }

    [Fact]
    public void ArrivingByKeyboardStillShows()
    {
        // The rail was the only thing keyboard focus coloured. Removing it
        // without putting a ring on the pill leaves the nav navigable and
        // invisible.
        XElement focused = Template()
            .Descendants()
            .Single(e => e.Name.LocalName == "Trigger"
                         && (string?)e.Attribute("Property") == "IsKeyboardFocused");

        Assert.Equal("1", (string?)Setter(focused, "Focus", "BorderThickness")?.Attribute("Value"));
    }

    [Fact]
    public void TheAccentFillsTheWholeRow()
    {
        // A Border paints its background INSIDE its border, so a ring carried on
        // the fill itself - even a transparent one - takes a pixel off every
        // edge. The handoff's pill is 172 by 32; with the ring on the fill it
        // measured 170 by 30 against the handoff's own screenshot. Nothing in
        // the markup looks wrong when that happens, which is why the rule is
        // written down rather than remembered: the fill carries no border, and
        // the ring is the Border over it.
        XElement fill = Template()
            .Descendants()
            .Single(e => e.Name.LocalName == "Border"
                         && (string?)e.Attribute(s_x + "Name") == "Fill");

        Assert.Null(fill.Attribute("BorderThickness"));
        Assert.Null(fill.Attribute("BorderBrush"));
    }

    [Fact]
    public void EachBandSetsItsRestingColourOnTheStyle()
    {
        // Not inherited from the band. A ToggleButton keeps WPF's default style
        // for its type even when an explicit Style is applied - the explicit one
        // replaces only the setters it declares - and a theme-style setter beats
        // property-value inheritance. Leaving Foreground off the style therefore
        // handed every unselected row SystemColors.ControlText, which is black,
        // and in the dark palette the whole nav went black on #2A2A2C: unreadable,
        // silent, and invisible in the markup. The colour is declared here so no
        // precedence rule has to be remembered correctly.
        Assert.Equal(
            "{DynamicResource SideNav.Foreground}",
            (string?)Setter(NavigationItemButton(), null, "Foreground")?.Attribute("Value"));

        Assert.Equal(
            "{DynamicResource SideNav.FootForeground}",
            (string?)Setter(FootItemButton(), null, "Foreground")?.Attribute("Value"));
    }

    [Fact]
    public void TheFootBandAsksForTheQuieterStyle()
    {
        // One DataTemplate draws both bands and it names NavigationItemButton, so
        // the foot band re-keys that name in its own resources. If this goes, the
        // three rows at the foot come out exactly as loud as the sections above.
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.Contains(
            "BasedOn=\"{StaticResource NavigationFootItemButton}\"",
            markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TextElement.Foreground", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCountOnTheOpenRowStepsBackFromWhite()
    {
        // The count is in the data template, which no template trigger can
        // reach, so it reads the button's state for itself. Left on the quiet
        // grey it would be all but unreadable on the blue.
        Assert.Contains(
            "{DynamicResource SideNav.SelectedCount}",
            Controls(),
            StringComparison.Ordinal);
    }

    private static string Controls() => File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

    private static XElement FootItemButton() =>
        XDocument.Load(AppMarkup.PathTo("Theme", "Controls.xaml"))
            .Descendants()
            .Single(e => e.Name.LocalName == "Style"
                         && (string?)e.Attribute(s_x + "Key") == "NavigationFootItemButton");

    private static XElement NavigationItemButton() =>
        XDocument.Load(AppMarkup.PathTo("Theme", "Controls.xaml"))
            .Descendants()
            .Single(e => e.Name.LocalName == "Style"
                         && (string?)e.Attribute(s_x + "Key") == "NavigationItemButton");

    private static XElement Template() =>
        NavigationItemButton()
            .Descendants()
            .Single(e => e.Name.LocalName == "ControlTemplate");

    private static XElement CheckedTrigger() =>
        Template()
            .Descendants()
            .Single(e => e.Name.LocalName == "Trigger"
                         && (string?)e.Attribute("Property") == "IsChecked");

    private static XElement? Setter(XElement trigger, string? target, string property) =>
        trigger.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Setter"
                                 && (string?)e.Attribute("TargetName") == target
                                 && (string?)e.Attribute("Property") == property);
}
