using System.ComponentModel;
using System.Xml.Linq;
using PhotoGallery.App.ViewModels;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The "Last scanned" cell of a folder's row says one thing at a time: the
/// date, "Scanning...", or "Not reachable".
/// </summary>
/// <remarks>
/// The three were stacked in one cell, and "Not reachable" was meant to be
/// painted over the date. A TextBlock has no background, so it painted nothing
/// out: with the photographs' share offline, the row read the red words and the
/// grey date on top of each other, unreadable. Re-scanning a folder that could
/// not be reached did the same with "Scanning..." and "Not reachable".
///
/// <para>Which one shows is the row's to say, and the cell binds each to its
/// own answer, so no two can ever be visible together.</para>
/// </remarks>
public sealed class LastScannedCellTests
{
    private static readonly XNamespace s_wpf =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Theory]
    [InlineData(false, false, "date")]
    [InlineData(true, false, "scanning")]
    [InlineData(false, true, "unreachable")]
    [InlineData(true, true, "scanning")]
    public void TheRowSaysExactlyOneThing(bool scanning, bool unavailable, string expected)
    {
        PhotoSourceItem row = new(1, @"\\server\photos", 10, new DateTime(2026, 9, 14, 13, 54, 0, DateTimeKind.Utc))
        {
            IsScanning = scanning,
            IsUnavailable = unavailable,
        };

        string[] showing =
        [
            .. row.ShowsLastScan ? ["date"] : Array.Empty<string>(),
            .. row.IsScanning ? ["scanning"] : Array.Empty<string>(),
            .. row.ShowsUnavailable ? ["unreachable"] : Array.Empty<string>(),
        ];

        Assert.Equal([expected], showing);
    }

    [Theory]
    [InlineData(nameof(PhotoSourceItem.IsScanning))]
    [InlineData(nameof(PhotoSourceItem.IsUnavailable))]
    public void EitherStateChangingRedrawsBothOfTheOthers(string changed)
    {
        PhotoSourceItem row = new(1, @"\\server\photos", 10, DateTime.UtcNow);
        List<string?> raised = [];
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        if (changed == nameof(PhotoSourceItem.IsScanning))
        {
            row.IsScanning = true;
        }
        else
        {
            row.IsUnavailable = true;
        }

        Assert.Contains(nameof(PhotoSourceItem.ShowsLastScan), raised);
        Assert.Contains(nameof(PhotoSourceItem.ShowsUnavailable), raised);
    }

    [Fact]
    public void EachWordingInTheCellHasItsOwnAnswer()
    {
        XElement column = XDocument.Load(AppMarkup.PathTo("Shell", "MainWindow.xaml"))
            .Descendants(s_wpf + "GridViewColumn")
            .Single(element => (string?)element.Attribute("Header") == "Last scanned");

        string[] shownWhen =
        [
            .. column.Descendants(s_wpf + "TextBlock")
                .Select(text => (string?)text.Attribute("Visibility") ?? "(always)")
                .Select(binding => binding.Split(',')[0].Replace("{Binding", string.Empty, StringComparison.Ordinal).Trim())
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [nameof(PhotoSourceItem.IsScanning), nameof(PhotoSourceItem.ShowsLastScan), nameof(PhotoSourceItem.ShowsUnavailable)],
            shownWhen);
    }
}
