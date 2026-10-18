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
