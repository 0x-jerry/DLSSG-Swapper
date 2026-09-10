using System.Windows;
using DlssgSwapper.App.ViewModels;
using Wpf.Ui.Appearance;

namespace DlssgSwapper.App;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.Mica);
    }
}