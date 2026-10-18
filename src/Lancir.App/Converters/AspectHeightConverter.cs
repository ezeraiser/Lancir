using System.Globalization;
using System.Windows.Data;

namespace Lancir.App.Converters;

/// <summary>Turns a width into a 16:9 height, so tiles scale with the space they get instead of wrapping to fewer rows.</summary>
public sealed class AspectHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width && width > 0 ? Math.Round(width * 9 / 16) : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
