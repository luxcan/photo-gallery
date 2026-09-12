namespace PhotoGallery.Tests.App;

/// <summary>
/// The colours themselves, pinned to the values the design handoff names.
/// </summary>
/// <remarks>
/// Until this existed, not one of the suite's assertions read a colour. The
/// parity test proved the two files held the same keys, the ramp test proved six
/// of them were ordered, and between them the whole palette could have gone back
/// to the teal it came from with a green run. A re-skin is a change nobody can
/// see in a stack trace, so the values are written down here and a later edit has
/// to mean it.
///
/// <para>Only the keys the handoff actually names are pinned. Everything derived
/// - the hovers, the tints, the scrollbar - is left free on purpose: pinning a
/// value nobody specified would turn a judgement call into a rule and make the
/// next tune of the palette look like a regression.</para>
/// </remarks>
public sealed class ThemeValueTests
{
    /// <summary>The light half of the handoff's token table.</summary>
    public static readonly TheoryData<string, string> Light = new()
    {
        { "Editor.Background", "#FFFFFFFF" },
        { "SideBar.Background", "#FFF5F5F7" },
        { "StatusBar.Background", "#FFF5F5F7" },
        { "Neutral.2", "#FFF5F5F7" },
        { "Panel.Border", "#FFE5E5EA" },
        { "Separator", "#FFE5E5EA" },
        { "Input.Border", "#FFD2D2D7" },
        { "Input.Background", "#FFFFFFFF" },
        { "Button.SecondaryBackground", "#FFFFFFFF" },
        { "Editor.Foreground", "#FF1D1D1F" },
        { "SideBar.Foreground", "#FF1D1D1F" },
        { "SideNav.Foreground", "#FF1D1D1F" },
        { "SideNav.FootForeground", "#FF3A3A3C" },
        { "TextCaption", "#FF86868B" },
        { "StatusBar.Foreground", "#FF86868B" },
        { "Editor.Placeholder", "#FF8E8E93" },
        { "TextDisabled", "#FF8E8E93" },
        { "Button.Background", "#FF007AFF" },
        { "Button.HoverBackground", "#FF0071E3" },
        { "Button.Foreground", "#FFFFFFFF" },
        { "Focus.Border", "#FF007AFF" },
        { "Link.Foreground", "#FF007AFF" },
        { "SideNav.SelectedCount", "#CCFFFFFF" },
        { "Badge.Background", "#FFE5E5EA" },
        { "Badge.Foreground", "#FF1D1D1F" },
        { "VideoBadge.Background", "#B81D1D1F" },
        { "VideoBadge.Foreground", "#FFFFFFFF" },
        { "Status.SuccessVivid", "#FF34C759" },
        { "Status.CautionVivid", "#FFFF9F0A" },
    };

    /// <summary>The dark half of it.</summary>
    public static readonly TheoryData<string, string> Dark = new()
    {
        { "Editor.Background", "#FF1E1E1E" },
        { "SideBar.Background", "#FF2A2A2C" },
        { "StatusBar.Background", "#FF2A2A2C" },
        { "Neutral.2", "#FF2A2A2C" },
        { "Panel.Border", "#FF3A3A3C" },
        { "Separator", "#FF3A3A3C" },
        { "Input.Border", "#FF48484A" },
        { "Input.Background", "#FF323234" },
        { "Button.SecondaryBackground", "#FF323234" },
        { "Editor.Foreground", "#FFF5F5F7" },
        { "SideBar.Foreground", "#FFF5F5F7" },
        { "SideNav.Foreground", "#FFF5F5F7" },
        { "SideNav.FootForeground", "#FFD1D1D6" },
        { "TextCaption", "#FF98989D" },
        { "StatusBar.Foreground", "#FF98989D" },
        { "Editor.Placeholder", "#FF98989D" },
        { "TextDisabled", "#FF8E8E93" },
        { "Button.Background", "#FF0A84FF" },
        { "Button.HoverBackground", "#FF097AE3" },
        { "Button.Foreground", "#FFFFFFFF" },
        { "Focus.Border", "#FF0A84FF" },
        { "Link.Foreground", "#FF0A84FF" },
        { "SideNav.SelectedCount", "#CCFFFFFF" },
        { "Badge.Background", "#FF48484A" },
        { "Badge.Foreground", "#FFF5F5F7" },
        { "VideoBadge.Background", "#B3000000" },
        { "VideoBadge.Foreground", "#FFF5F5F7" },
        { "Status.SuccessVivid", "#FF30D158" },
        { "Status.CautionVivid", "#FFFF9F0A" },
    };

    [Theory]
    [MemberData(nameof(Light))]
    public void TheLightPaletteIsTheOneTheHandoffNames(string key, string hex) =>
        Assert.Equal(hex, ThemePalette.Of("Light.xaml")[key]);

    [Theory]
    [MemberData(nameof(Dark))]
    public void TheDarkPaletteIsTheOneTheHandoffNames(string key, string hex) =>
        Assert.Equal(hex, ThemePalette.Of("Dark.xaml")[key]);

    [Fact]
    public void TextOnTheAccentIsWhiteInBothThemes()
    {
        // The one role that reversed rather than recoloured. Dark's accent
        // foreground used to be a near-black, because the accent it sat on was a
        // bright teal; on system blue that is black text on a blue button, on
        // every primary button, the open nav row and the chosen segment at once -
        // and nothing in the app would have said so.
        Assert.Equal("#FFFFFFFF", ThemePalette.Of("Light.xaml")["Button.Foreground"]);
        Assert.Equal("#FFFFFFFF", ThemePalette.Of("Dark.xaml")["Button.Foreground"]);
    }

    [Theory]
    [InlineData("Light.xaml")]
    [InlineData("Dark.xaml")]
    public void APanelIsOutlinedMoreQuietlyThanAnInput(string file)
    {
        // These were one token for as long as the palette gave them one value.
        // The handoff separates them - a card or a band is ruled off in the
        // divider, and only something you can type in or press carries the
        // heavier edge - and a token map that quietly rejoined them would draw
        // every card border a step too dark with nothing to show for it.
        IReadOnlyDictionary<string, string> palette = ThemePalette.Of(file);

        Assert.NotEqual(palette["Panel.Border"], palette["Input.Border"]);
        Assert.Equal(palette["Separator"], palette["Panel.Border"]);
    }

    [Theory]
    [InlineData("Light.xaml")]
    [InlineData("Dark.xaml")]
    public void TheBadgeOnAPhotographIsDarkInBothThemes(string file)
    {
        // It is read against a picture rather than against the page, so it does
        // not follow the theme the way a chip on a card does. Pointing it back at
        // Badge.Background puts a pale chip on a pale photograph, which is how it
        // was before the two were separated. Only the fill is asserted: in dark
        // the two foregrounds genuinely agree, because the page's own text
        // colour is already what belongs on a black scrim.
        IReadOnlyDictionary<string, string> palette = ThemePalette.Of(file);

        Assert.NotEqual(palette["Badge.Background"], palette["VideoBadge.Background"]);
        Assert.True(
            Luminance(palette["VideoBadge.Background"]) < 0.2,
            "the badge on a photograph has to be a dark scrim in both themes");
    }

    /// <summary>Rec. 709 relative luminance of a #AARRGGBB string.</summary>
    private static double Luminance(string hex)
    {
        string rgb = hex.TrimStart('#')[2..];

        return (0.2126 * (Convert.ToInt32(rgb[..2], 16) / 255d))
             + (0.7152 * (Convert.ToInt32(rgb[2..4], 16) / 255d))
             + (0.0722 * (Convert.ToInt32(rgb[4..6], 16) / 255d));
    }

    [Theory]
    [InlineData("Light.xaml")]
    [InlineData("Dark.xaml")]
    public void TheFootNavIsAStepBackFromTheSectionsAboveIt(string file)
    {
        // Its own key rather than SideBar.Foreground, which is also the label on
        // every SecondaryButton - and the artboards draw those at full strength
        // while drawing the three rows at the foot quieter. Pointing one key at
        // both dimmed every button label in the app to make three rows right.
        IReadOnlyDictionary<string, string> palette = ThemePalette.Of(file);

        Assert.Equal(palette["SideNav.Foreground"], palette["SideBar.Foreground"]);
        Assert.NotEqual(palette["SideNav.Foreground"], palette["SideNav.FootForeground"]);
    }

    [Fact]
    public void ThePaletteIsNotTrivial()
    {
        // Guards against every assertion above passing because the reader
        // returned an empty table.
        Assert.True(ThemePalette.Of("Light.xaml").Count > 100);
        Assert.True(ThemePalette.Of("Dark.xaml").Count > 100);
    }
}
