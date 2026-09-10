using System.ComponentModel;
using System.Windows;
using DlssgSwapper.App.Configuration;
using DlssgSwapper.App.Navigation;
using DlssgSwapper.App.ViewModels;
using DlssgSwapper.App.Views.Pages;
using Wpf.Ui.Controls;

namespace DlssgSwapper.App;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        RootNavigation.SetPageProviderService(new AppPageProvider(CreatePage));
        _viewModel.NavigationRequested += OnNavigationRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        RootNavigation.Navigate(typeof(GamesPage));
        ThemeService.Apply(_viewModel.SelectedTheme, this, WindowBackdropType.Mica);
    }

    private object? CreatePage(Type pageType)
    {
        if (Activator.CreateInstance(pageType) is not FrameworkElement page) return null;
        page.DataContext = _viewModel;
        return page;
    }

    private void OnNavigationRequested(AppSection section)
    {
        if (section == AppSection.Install && !_viewModel.HasSelectedProfile) return;
        RootNavigation.Navigate(SectionPage(section));
    }

    private static Type SectionPage(AppSection section) => section switch
    {
        AppSection.Install => typeof(InstallPage),
        AppSection.Settings => typeof(SettingsPage),
        AppSection.About => typeof(AboutPage),
        _ => typeof(GamesPage),
    };

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTheme))
            ThemeService.Apply(_viewModel.SelectedTheme, this, WindowBackdropType.Mica);
    }
}
