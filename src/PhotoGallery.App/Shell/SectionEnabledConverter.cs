using System.Globalization;
using System.Windows.Data;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Enables a side-nav section unless it needs photos and there are none.
/// </summary>
/// <remarks>
/// Values are (RequiresSources, HasSources, RequiresFaces, FacesAvailable).
/// Computed rather than stored on the section so there is no second copy of the
/// state to keep in step.
///
/// <para>Two gates rather than one, because a section can fail either: Library
/// needs photographs, and People needs photographs <em>and</em> a model that
/// does not ship with the app.</para>
/// </remarks>
public sealed class SectionEnabledConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not bool requiresSources)
        {
            return true;
        }

        if (requiresSources && values[1] is not true)
        {
            return false;
        }

        // Missing entirely rather than false, so a caller written against the
        // two-value form keeps working instead of disabling everything.
        return values.Length < 4 || values[2] is not true || values[3] is true;
    }

    public object[] ConvertBack(
        object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
