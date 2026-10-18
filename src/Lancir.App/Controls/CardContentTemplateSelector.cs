using System.Windows;
using System.Windows.Controls;
using Lancir.App.ViewModels;

namespace Lancir.App.Controls;

/// <summary>Picks the content template for a details-panel card by its id: the resource "Card.{id}".</summary>
public sealed class CardContentTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not DetailCardViewModel card)
        {
            return null;
        }

        var key = $"Card.{card.Id}";
        return (container as FrameworkElement)?.TryFindResource(key) as DataTemplate
               ?? Application.Current.TryFindResource(key) as DataTemplate;
    }
}
