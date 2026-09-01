using System.Globalization;
using System.Windows.Data;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Shell;

/// <summary>
/// How much a section holds, said beside its name while the nav is open.
/// </summary>
/// <remarks>
/// Values are (Key, LibraryCounts, source count). Computed here rather than
/// stored on the section for the same reason the enabled state is: there is no
/// second copy to keep in step. Sections that count nothing - About, Settings -
/// and counts of nought say nothing at all, so a library with no photos in it
/// does not open onto a column of zeroes.
/// </remarks>
public sealed class SectionCountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not string key)
        {
            return string.Empty;
        }

        LibraryCounts counts = values[1] as LibraryCounts ?? LibraryCounts.Empty;
        int sources = values[2] is int n ? n : 0;

        int count = key switch
        {
            ActivitySection.LibraryKey => counts.TotalAssets,
            ActivitySection.PeopleKey => counts.People,
            ActivitySection.AlbumsKey => counts.Albums,
            ActivitySection.DuplicatesKey => counts.UnresolvedDuplicateSets,
            ActivitySection.SourcesKey => sources,
            _ => 0,
        };

        // The machine's culture, not the binding's. A WPF binding's culture comes
        // from the element's Language, which defaults to en-US whatever Windows
        // is set to - so the count beside Library would separate its thousands
        // differently from the same number in the sources table two clicks away.
        return count == 0
            ? string.Empty
            : count.ToString("N0", CultureInfo.CurrentCulture);
    }

    public object[] ConvertBack(
        object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
