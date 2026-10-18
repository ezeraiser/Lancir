using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lancir.App.Converters;

/// <summary>
/// Multi-value converter for a resizable side panel's column width: values are [isOpen (bool),
/// paneWidth (GridLength)]. Returns paneWidth when open, zero width when closed - so the splitter
/// and panel collapse to nothing rather than leaving an empty gap, while still remembering the
/// user's chosen width for next time the panel opens.
/// </summary>
public sealed class PaneOpenWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [bool isOpen, ..])
        {
            return new GridLength(0);
        }

        if (!isOpen)
        {
            return new GridLength(0);
        }

        return values.Length > 1 && values[1] is GridLength width ? width : new GridLength(400);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        // The GridSplitter writes the resized column width back here. Only the pane-width side
        // (index 1) is writable; HasSelectedGame (index 0) is left unset so its binding is untouched.
        return [Binding.DoNothing, value];
    }
}
