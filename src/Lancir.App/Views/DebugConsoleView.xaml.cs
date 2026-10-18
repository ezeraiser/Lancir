using System.Collections.Specialized;
using System.Windows.Controls;
using Lancir.App.ViewModels;

namespace Lancir.App.Views;

public partial class DebugConsoleView : UserControl
{
    public DebugConsoleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>False when the view sits inside Settings, which has its own title.</summary>
    public bool ShowTitle
    {
        get => TitleText.Visibility == System.Windows.Visibility.Visible;
        set => TitleText.Visibility = value ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DebugConsoleViewModel oldViewModel)
        {
            oldViewModel.Lines.CollectionChanged -= OnLinesChanged;
        }

        if (e.NewValue is DebugConsoleViewModel newViewModel)
        {
            newViewModel.Lines.CollectionChanged += OnLinesChanged;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        LogScrollViewer.ScrollToBottom();
    }
}
