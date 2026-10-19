using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Lancir.App.Views;

/// <summary>A message with a few buttons under each other; tells which one was pressed (the first is the suggested one).</summary>
public partial class ChoiceDialogWindow : Window
{
    private int _choice = -1;

    public ChoiceDialogWindow(string title, string message, IReadOnlyList<string> choices)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        for (var i = 0; i < choices.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = choices[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Appearance = i == 0 ? ControlAppearance.Primary : ControlAppearance.Secondary,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12, 8, 12, 8),
            };
            button.Click += (_, _) =>
            {
                _choice = index;
                DialogResult = true;
            };
            Buttons.Children.Add(button);
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    /// <summary>Shows the dialog over the main window; the index of the pressed button, or -1 when it was closed.</summary>
    public static int Ask(string title, string message, params string[] choices)
    {
        var window = new ChoiceDialogWindow(title, message, choices) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window._choice : -1;
    }
}
