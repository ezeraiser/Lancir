using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lancir.App.Converters;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null && !(value is string s && string.IsNullOrWhiteSpace(s)) &&
                       !(value is bool b && !b) && !(value is int i && i == 0);
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);

        return hasValue != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
