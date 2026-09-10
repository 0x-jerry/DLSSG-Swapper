using System.IO;
using System.Windows;
using System.Windows.Controls;
using DlssgSwapper.Core.Steam;
using Wpf.Ui.Controls;

namespace DlssgSwapper.App.Views;

public partial class SteamScanWindow
{
    private sealed record ExeRow(CandidateExe Exe, string DisplayPath);

    private readonly List<SteamApp> _apps = new();
    private bool _scanning;

    public string? SelectedGameName { get; private set; }
    public string? SelectedExe { get; private set; }

    public SteamScanWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ScanAsync();

    private async void OnRescan(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        ScanRing.Visibility = Visibility.Visible;
        ShowStatus(InfoBarSeverity.Informational, "Scanning Steam libraries…", "Reading the folders Steam has registered.");

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
                GamesList.ItemsSource = null;
                ShowStatus(InfoBarSeverity.Warning, "Steam was not found",
                           "Steam is not registered on this machine. Add the game manually instead.");
                return;
            }

            _apps.AddRange(apps);
            GamesList.ItemsSource = _apps;
            ShowStatus(
                _apps.Count > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                $"Found {_apps.Count} installed {(_apps.Count == 1 ? "game" : "games")}",
                _apps.Count > 0 ? "Select a game, then its rendering executable." : "No Steam library entries were readable.");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Scan failed", ex.Message);
        }
        finally
        {
            _scanning = false;
            ScanRing.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        ScanStatus.Severity = severity;
        ScanStatus.Title = title;
        ScanStatus.Message = message;
        ScanStatus.IsOpen = true;
    }

    private void OnGameSelected(object sender, SelectionChangedEventArgs e)
    {
        var app = GamesList.SelectedItem as SteamApp;
        var exes = app == null ? null : SteamLibraryScanner.FindCandidateExecutables(app.InstallDir);

        ExesList.ItemsSource = exes?.Select(exe => new ExeRow(exe, RelativePath(exe.Path, app!.InstallDir))).ToList();
        OkButton.IsEnabled = false;
        ExesHint.Text = exes switch
        {
            null => "Select a game to list its executables.",
            { Count: 0 } => "No executable found in this folder. Try another game.",
            _ => "",
        };
    }

    private static string RelativePath(string path, string root) =>
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : path;

    private void OnExeSelected(object sender, SelectionChangedEventArgs e)
    {
        OkButton.IsEnabled = GamesList.SelectedItem is SteamApp && ExesList.SelectedItem != null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is SteamApp app && ExesList.SelectedItem is ExeRow row)
        {
            SelectedGameName = app.Name;
            SelectedExe = row.Exe.Path;
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
