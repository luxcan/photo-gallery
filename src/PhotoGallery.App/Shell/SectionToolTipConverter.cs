using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Tooltip for a side-nav section: why it is disabled, or - when the nav is
/// folded and the name is gone - its name.
/// </summary>
/// <remarks>
/// Values are (Title, RequiresSources, HasSources, IsNavCollapsed, RequiresFaces,
/// FacesAvailable). The disabled reason is checked first and said in both
/// states, because it explains why the item will not respond rather than
/// standing in for a missing name - and since it already contains the title, one
/// string covers both jobs at once.
///
/// <para>Photographs are named before models, because a library with neither
/// cannot use a model even once it has one: sending somebody off to download
/// 182 MB before they have added a folder would be the wrong first instruction.</para>
///
/// <para>Open and enabled there is nothing left to say: the name is an inch to
/// the right of the pointer, and repeating it is noise.</para>
/// </remarks>
public sealed class SectionToolTipConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        string title = values.Length > 0 && values[0] is string t ? t : string.Empty;
        bool requiresSources = values.Length > 1 && values[1] is true;
        bool hasSources = values.Length > 2 && values[2] is true;

        // Three values still means "always name it", which is what a caller
        // written against the icon-only bar assumed.
        bool isCollapsed = values.Length < 4 || values[3] is true;

        if (requiresSources && !hasSources)
        {
            return $"{title} - add a photo folder in Library first";
        }

        bool requiresFaces = values.Length > 4 && values[4] is true;
        bool facesAvailable = values.Length < 6 || values[5] is true;

        if (requiresFaces && !facesAvailable)
        {
            return $"{title} - install the face model in Settings first";
        }

        // UnsetValue rather than null: the target falls back to its own default,
        // so there is no tooltip at all rather than an empty grey box.
        return isCollapsed ? title : DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(
        object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
