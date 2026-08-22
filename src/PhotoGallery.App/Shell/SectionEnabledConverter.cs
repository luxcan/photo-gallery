using System.Globalization;
using System.Windows.Data;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Enables a side-nav section unless it needs photos and there are none.
/// </summary>
/// <remarks>
/// Values are (RequiresSources, HasSources). Computed rather than stored on the
/// section so there is no second copy of the state to keep in step.
/// </remarks>
public sealed class SectionEnabledConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not bool requiresSources)
        {
            return true;
        }

        return !requiresSources || values[1] is true;
    }

    public object[] ConvertBack(
        object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
