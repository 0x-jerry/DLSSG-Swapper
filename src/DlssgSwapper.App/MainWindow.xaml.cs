using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DlssgSwapper.App.Configuration;
using DlssgSwapper.App.Navigation;
using DlssgSwapper.App.ViewModels;
using DlssgSwapper.App.Views.Pages;
using Wpf.Ui.Controls;

namespace DlssgSwapper.App;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel = new();
    private readonly List<AppSection> _history = new();
    private int _historyIndex = -1;
    private bool _replayingHistory;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        RootNavigation.SetPageProviderService(new AppPageProvider(CreatePage));
        RootNavigation.Navigated += OnNavigated;
        _viewModel.NavigationRequested += OnNavigationRequested;
        _viewModel.BackRequested += NavigateBack;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        PreviewMouseDown += OnPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;

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

    private static AppSection SectionOf(Type pageType) => pageType switch
    {
        var type when type == typeof(InstallPage) => AppSection.Install,
        var type when type == typeof(SettingsPage) => AppSection.Settings,
        var type when type == typeof(AboutPage) => AppSection.About,
        _ => AppSection.Games,
    };

    private void OnNavigated(object sender, NavigatedEventArgs e)
    {
        if (_replayingHistory || e.Page is not FrameworkElement page) return;

        var section = SectionOf(page.GetType());
        if (_historyIndex >= 0 && _history[_historyIndex] == section) return;

        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(section);
        _historyIndex = _history.Count - 1;
    }

    private void NavigateBack()
    {
        if (_historyIndex <= 0) return;
        ReplayHistory(_historyIndex - 1);
    }

    private void NavigateForward()
    {
        if (_historyIndex >= _history.Count - 1) return;
        ReplayHistory(_historyIndex + 1);
    }

    private void ReplayHistory(int index)
    {
        _historyIndex = index;
        _replayingHistory = true;
        try
        {
            RootNavigation.Navigate(SectionPage(_history[index]));
        }
        finally
        {
            _replayingHistory = false;
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            NavigateForward();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool alt = e.KeyboardDevice.Modifiers == ModifierKeys.Alt;

        if (key == Key.BrowserBack || (alt && key == Key.Left))
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (key == Key.BrowserForward || (alt && key == Key.Right))
        {
            NavigateForward();
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTheme))
            ThemeService.Apply(_viewModel.SelectedTheme, this, WindowBackdropType.Mica);
    }
}
