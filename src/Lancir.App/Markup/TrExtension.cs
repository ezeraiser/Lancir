using System.Windows.Markup;
using Lancir.App.Services;

namespace Lancir.App.Markup;

/// <summary>XAML translation: <c>Text="{loc:Tr 'Kütüphane'}"</c> shows the English text when the app language is English.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension(string text)
    {
        Text = text;
    }

    public string Text { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
