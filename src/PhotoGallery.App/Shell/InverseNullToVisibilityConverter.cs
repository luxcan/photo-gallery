using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoGallery.App.Shell;

/// <summary>Shows an element only when the bound value is there.</summary>
/// <remarks>
/// The other way round from <see cref="NullToVisibilityConverter"/>, which draws
/// an empty state in place of something missing. This one is for the frame that
/// has nothing to draw and should take up no room at all - a picture that was
/// never prepared has no cached copy, and a blank box where a photograph should
/// be reads as the app having lost it.
/// </remarks>
public sealed class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(
        object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
