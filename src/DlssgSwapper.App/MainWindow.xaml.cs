using System.ComponentModel;
using DlssgSwapper.App.Configuration;
using DlssgSwapper.App.ViewModels;
using DlssgSwapper.App.Views.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace DlssgSwapper.App;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _syncingSelection;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        _viewModel.NavigationRequested += NavigateTo;
        _viewModel.BackRequested += NavigateBack;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ContentFrame.Navigated += OnNavigated;
        RootGrid.PointerPressed += OnPointerPressed;
        AddAccelerator(VirtualKey.GoBack, VirtualKeyModifiers.None, NavigateBack);
        AddAccelerator(VirtualKey.GoForward, VirtualKeyModifiers.None, NavigateForward);
        AddAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu, NavigateBack);
        AddAccelerator(VirtualKey.Right, VirtualKeyModifiers.Menu, NavigateForward);

        SyncSelection(AppSection.Games);
        NavigateTo(AppSection.Games);
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is FrameworkElement page) page.DataContext = _viewModel;
        SyncSelection(SectionOf(e.SourcePageType));
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection || args.SelectedItem is not NavigationViewItem item) return;
        NavigateTo(SectionOf(item.Tag as string));
    }

    private void SyncSelection(AppSection section)
    {
        _syncingSelection = true;
        RootNavigation.SelectedItem = ItemFor(section);
        _syncingSelection = false;
    }

    private void NavigateTo(AppSection section)
    {
        if (section == AppSection.Install && !_viewModel.HasSelectedProfile) return;

        var pageType = PageFor(section);
        if (ContentFrame.CurrentSourcePageType == pageType) return;
        ContentFrame.Navigate(pageType);
    }

    private void NavigateBack()
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void NavigateForward()
    {
        if (ContentFrame.CanGoForward) ContentFrame.GoForward();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(RootGrid).Properties;
        if (props.IsXButton1Pressed)
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (props.IsXButton2Pressed)
        {
            NavigateForward();
            e.Handled = true;
        }
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTheme))
            ThemeService.Apply(_viewModel.SelectedTheme, this);
    }

    private static AppSection SectionOf(string? tag) => tag switch
    {
        "Install" => AppSection.Install,
        "Settings" => AppSection.Settings,
        "About" => AppSection.About,
        _ => AppSection.Games,
    };

    private static AppSection SectionOf(Type pageType) => pageType switch
    {
        var t when t == typeof(InstallPage) => AppSection.Install,
        var t when t == typeof(SettingsPage) => AppSection.Settings,
        var t when t == typeof(AboutPage) => AppSection.About,
        _ => AppSection.Games,
    };

    private static Type PageFor(AppSection section) => section switch
    {
        AppSection.Install => typeof(InstallPage),
        AppSection.Settings => typeof(SettingsPage),
        AppSection.About => typeof(AboutPage),
        _ => typeof(GamesPage),
    };

    private NavigationViewItem? ItemFor(AppSection section)
    {
        string tag = section.ToString();
        return RootNavigation.MenuItems.Concat(RootNavigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => (item.Tag as string) == tag);
    }
}
