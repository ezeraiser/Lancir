using System.Windows;
using System.Windows.Controls;

namespace Lancir.App.ViewModels;

/// <summary>Picks the text-vs-image row template for a comparison field based on <see cref="MatchCompareFieldViewModel.IsImage"/>.</summary>
public sealed class MatchCompareFieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }

    public DataTemplate? ImageTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is MatchCompareFieldViewModel { IsImage: true })
        {
            return ImageTemplate;
        }

        return TextTemplate;
    }
}
