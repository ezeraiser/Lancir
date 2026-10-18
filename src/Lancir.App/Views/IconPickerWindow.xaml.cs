using System.Windows;
using Lancir.App.Services;
using Wpf.Ui.Controls;

namespace Lancir.App.Views;

/// <summary>What the user chose for a library's icon.</summary>
public sealed record IconChoice(SymbolRegular? Symbol, string? PicturePath);

/// <summary>Picks a library's icon from the built-in symbols, or loads a picture of the user's own.</summary>
public partial class IconPickerWindow : Window
{
    private IconChoice? _choice;

    public IconPickerWindow()
    {
        InitializeComponent();
        Title = Loc.T("Kütüphane simgesi");
        PromptText.Text = Loc.T("Bir simge seçin ya da kendi resminizi yükleyin.");
        UploadButton.Content = Loc.T("Resim yükle…");
        ResetButton.Content = Loc.T("Varsayılan simge");
        CancelButton.Content = Loc.T("İptal");
        SymbolList.ItemsSource = LibraryIcons.Symbols;
    }

    private void OnSymbolClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SymbolRegular symbol })
        {
            _choice = new IconChoice(symbol, null);
            DialogResult = true;
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _choice = new IconChoice(null, null);
        DialogResult = true;
    }

    private void OnUploadClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Simge olarak resim seç"),
            Filter = Loc.T("Resimler") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = LibraryIcons.Import(dialog.FileName);
        if (path is null)
        {
            ErrorText.Text = Loc.T("Bu dosya resim olarak okunamadı.");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _choice = new IconChoice(null, path);
        DialogResult = true;
    }

    /// <summary>Shows the picker over the main window; null when cancelled.</summary>
    public static IconChoice? Pick()
    {
        var window = new IconPickerWindow { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window._choice : null;
    }
}
