using System.Globalization;
using System.Windows.Data;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Width left over for a table's flexible column once the fixed ones are taken.
/// </summary>
/// <remarks>
/// GridView has no star sizing, so the stretching column is computed from the
/// list's own width. The parameter is the total of the fixed columns plus the
/// scrollbar and border allowance.
/// </remarks>
public sealed class RemainingWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double total || double.IsNaN(total))
        {
            return 0d;
        }

        double reserved = parameter is string text
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0d;

        // Never negative: a very narrow window would otherwise throw.
        return Math.Max(80d, total - reserved);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
