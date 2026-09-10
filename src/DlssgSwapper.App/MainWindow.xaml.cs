using System.Windows;
using DlssgSwapper.App.ViewModels;

namespace DlssgSwapper.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }
}