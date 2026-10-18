using System.Windows;
using System.Windows.Input;
using Lancir.App.Services;

namespace Lancir.App.Views;

/// <summary>A small dialog that asks for one line of text (a library's name).</summary>
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initial;
        OkButton.Content = Loc.T("Tamam");
        CancelButton.Content = Loc.T("İptal");
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string Value => InputBox.Text.Trim();

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
        }
    }

    /// <summary>Shows the dialog over the main window; null when cancelled or left empty.</summary>
    public static string? Ask(string title, string prompt, string initial = "")
    {
        var window = new TextPromptWindow(title, prompt, initial) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true && window.Value.Length > 0 ? window.Value : null;
    }
}
