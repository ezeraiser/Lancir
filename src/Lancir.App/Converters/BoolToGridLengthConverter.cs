using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lancir.App.Converters;

/// <summary>
/// Converts a bool to one of two GridLength values passed as "trueValue|falseValue" in
/// ConverterParameter (e.g. "56|220"). Used to animate a collapsible nav column's width.
/// </summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isTrue || parameter is not string spec)
        {
            return GridLength.Auto;
        }

        var parts = spec.Split('|');
        var chosen = isTrue ? parts[0] : parts[1];
        return double.TryParse(chosen, out var pixels) ? new GridLength(pixels) : GridLength.Auto;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
