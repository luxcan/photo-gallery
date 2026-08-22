using System.Globalization;
using System.Windows.Data;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Negates a boolean, so two mutually exclusive options can share one property.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag && !flag;

    public object ConvertBack(
        object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag && !flag;
}
