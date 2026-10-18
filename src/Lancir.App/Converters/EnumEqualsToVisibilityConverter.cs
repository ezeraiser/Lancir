using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lancir.App.Converters;

/// <summary>Shows the bound element when the value equals the converter parameter (both compared as strings).</summary>
public sealed class EnumEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return Visibility.Collapsed;
        }

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
