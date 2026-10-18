using Lancir.App.ViewModels;
using System.Windows.Controls;

namespace Lancir.App.Views;

public partial class ScanReviewView : UserControl
{
    public ScanReviewView()
    {
        InitializeComponent();
    }

    /// <summary>False when the view sits inside Settings, which has its own title.</summary>
    public bool ShowTitle
    {
        get => TitleText.Visibility == System.Windows.Visibility.Visible;
        set => TitleText.Visibility = value ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
}
