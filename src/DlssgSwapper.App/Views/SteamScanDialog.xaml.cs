using DlssgSwapper.Core.Steam;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DlssgSwapper.App.Views;

public sealed partial class SteamScanDialog : ContentDialog
{
    public sealed record ExeRow(CandidateExe Exe, string DisplayPath);

    private readonly List<SteamApp> _apps = new();
    private bool _scanning;

    public string? SelectedGameName { get; private set; }
    public string? SelectedExe { get; private set; }

    public SteamScanDialog()
    {
        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 900d;
        Opened += (_, _) => _ = ScanAsync();
    }

    private async void OnRescan(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        ScanRing.IsActive = true;
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
            ScanRing.IsActive = false;
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
        IsPrimaryButtonEnabled = false;
        ExesHint.Text = exes switch
        {
            null => "Select a game to list its executables.",
            { Count: 0 } => "No executable found in this folder. Try another game.",
            _ => "",
        };
        ExesHint.Visibility = exes is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string RelativePath(string path, string root) =>
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : path;

    private void OnExeSelected(object sender, SelectionChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = GamesList.SelectedItem is SteamApp && ExesList.SelectedItem is ExeRow;
    }

    private void OnAddGame(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (GamesList.SelectedItem is SteamApp app && ExesList.SelectedItem is ExeRow row)
        {
            SelectedGameName = app.Name;
            SelectedExe = row.Exe.Path;
            return;
        }
        args.Cancel = true;
    }
}
