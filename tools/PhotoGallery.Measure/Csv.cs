using System.Globalization;

namespace PhotoGallery.Measure;

/// <summary>Writes one CSV row, quoting only where a field needs it.</summary>
internal static class Csv
{
    public static string Row(params object?[] fields) =>
        string.Join(',', fields.Select(Field));

    private static string Field(object? value)
    {
        string text = value switch
        {
            null => string.Empty,
            double number when !double.IsFinite(number) => string.Empty,
            double number => number.ToString("G6", CultureInfo.InvariantCulture),
            float number => number.ToString("G6", CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }
}
