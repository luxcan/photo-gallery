using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoGallery.App.Shell;

/// <summary>Shows an element only when the bound value is missing.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(
        object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
