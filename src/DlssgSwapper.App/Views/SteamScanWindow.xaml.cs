using System.Windows;
using System.Windows.Controls;
using DlssgSwapper.Core.Steam;
using Wpf.Ui.Appearance;

namespace DlssgSwapper.App.Views;

public partial class SteamScanWindow
{
    private readonly List<SteamApp> _apps = new();

    public string? SelectedGameName { get; private set; }
    public string? SelectedExe { get; private set; }

    public SteamScanWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.None);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ScanAsync();

    private async void OnRescan(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        GamesList.IsEnabled = false;
        StatusText.Text = "Scanning Steam libraries…";
        try
        {
            var apps = await Task.Run(() =>
            {
                string? steam = SteamLibraryScanner.FindSteamInstallDir();
                if (steam == null) return null;
                return SteamLibraryScanner.FindInstalledApps(SteamLibraryScanner.FindLibraryRoots(steam));
            });

            _apps.Clear();
            if (apps == null)
            {
                StatusText.Text = "Steam installation not found in the registry. Add the game manually instead.";
                GamesList.ItemsSource = null;
                return;
            }
            _apps.AddRange(apps);
            GamesList.ItemsSource = _apps;
            StatusText.Text = $"Select a game, then its rendering executable. Found {_apps.Count} installed games.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            GamesList.IsEnabled = true;
        }
    }

    private void OnGameSelected(object sender, SelectionChangedEventArgs e)
    {
        ExesList.ItemsSource = GamesList.SelectedItem is SteamApp app
            ? SteamLibraryScanner.FindCandidateExecutables(app.InstallDir)
            : null;
        OkButton.IsEnabled = false;
    }

    private void OnExeSelected(object sender, SelectionChangedEventArgs e)
    {
        OkButton.IsEnabled = GamesList.SelectedItem is SteamApp && ExesList.SelectedItem != null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is SteamApp app && ExesList.SelectedItem is CandidateExe exe)
        {
            SelectedGameName = app.Name;
            SelectedExe = exe.Path;
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}