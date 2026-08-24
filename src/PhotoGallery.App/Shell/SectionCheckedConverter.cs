using System.Globalization;
using System.Windows.Data;

namespace PhotoGallery.App.Shell;

/// <summary>
/// True when a side-nav row is the selected section, giving the group
/// radio-button behaviour without any code tracking which button is down.
/// </summary>
public sealed class SectionCheckedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(
        object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
