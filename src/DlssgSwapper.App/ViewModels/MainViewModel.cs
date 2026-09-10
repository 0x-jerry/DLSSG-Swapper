using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DlssgSwapper.App.Configuration;
using DlssgSwapper.App.Mvvm;
using DlssgSwapper.App.Views;
using DlssgSwapper.Core.Configuration;
using DlssgSwapper.Core.Diagnostics;
using DlssgSwapper.Core.Games;
using DlssgSwapper.Core.Hardware;
using DlssgSwapper.Core.Payloads;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace DlssgSwapper.App.ViewModels;

public enum Preset
{
    Default,
    Performance,
}

public sealed record VerificationReport(string Headline, IReadOnlyList<string> Lines);

public enum AppSection
{
    Games,
    Install,
    Settings,
    About,
}

public sealed class GameProfileViewModel : ObservableObject
{
    public GameProfileViewModel(GameProfile profile, InstallInspection inspection)
    {
        Profile = profile;
        Inspection = inspection;
    }

    public GameProfile Profile { get; set; }
    public InstallInspection Inspection { get; set; }

    public void Update(GameProfile profile, InstallInspection inspection)
    {
        Profile = profile;
        Inspection = inspection;
        OnPropertyChanged(string.Empty);
    }

    public string Name => Profile.Name;
    public string ExecutablePath => Profile.ExecutablePath;
    public string ExecutableDirectory => Path.GetDirectoryName(Profile.ExecutablePath) ?? "";
    public string SourceName => Profile.Source == GameSource.Steam ? "Steam" : "Manual";

    public string InstallState => Inspection.Kind switch
    {
        InstallKind.Installed => "Installed",
        InstallKind.Partial => "Partial",
        _ => "Not installed",
    };

    public string InstallSummary => Inspection.Kind switch
    {
        InstallKind.Installed =>
            $"Installed {Inspection.InstalledVersion?.Version ?? "?"} via {Inspection.InstalledEntryPoint?.FileName ?? "?"}",
        InstallKind.Partial => "Partial install (proxy or INI missing)",
        _ => "Not installed",
    };
}

public sealed class MainViewModel : ObservableObject
{
    private readonly ProfileStore _store = ProfileStore.OpenDefault();
    private readonly PayloadCatalog _catalog;
    private readonly InstallationService _service;
    private readonly AppSettings _settings = AppSettings.Load();

    private GameProfileViewModel? _selectedProfile;
    private PayloadVersion? _selectedVersion;
    private PayloadEntryPoint? _selectedEntryPoint;
    private Preset _selectedPreset = Preset.Default;
    private string _selectedRouter = "SM86";
    private string _selectedKernelImage = "PTX";
    private int _selectedMaxFrames = 3;
    private int _selectedLoggingLevel = 1;
    private bool _hardwareBilinear;
    private bool _overwriteForeign;
    private string _gpuSummary = "Detecting GPU…";
    private string _gpuRouterSuggestion = "";
    private string _statusText = "Add or select a game to begin.";
    private VerificationReport? _verification;

    public MainViewModel()
    {
        _catalog = PayloadCatalog.Load(Path.Combine(AppContext.BaseDirectory, "payloads"));
        _service = new InstallationService(_catalog);
        _overwriteForeign = _settings.DefaultOverwriteForeign;

        Versions = new ObservableCollection<PayloadVersion>(_catalog.Versions);
        Routers = new ObservableCollection<string> { "SM86", "SM75" };
        KernelImages = new ObservableCollection<string> { "PTX", "Auto", "Cubin" };
        MaxFrames = new ObservableCollection<int> { 0, 1, 2, 3 };
        LoggingLevels = new ObservableCollection<int> { 0, 1, 2, 3 };
        Presets = new ObservableCollection<Preset> { Preset.Default, Preset.Performance };

        AddGameCommand = new RelayCommand(AddGame);
        ScanSteamCommand = new RelayCommand(ScanSteam);
        RemoveGameCommand = new RelayCommand(RemoveGame, () => SelectedProfile != null);
        ConfigureGameCommand = new RelayCommand<GameProfileViewModel>(ConfigureGame);
        ShowGamesCommand = new RelayCommand(() => NavigationRequested?.Invoke(AppSection.Games));
        CopyStatusCommand = new RelayCommand(CopyStatus);
        OpenGameFolderCommand = new RelayCommand(OpenGameFolder, () => SelectedProfile != null);
        OpenBackupFolderCommand = new RelayCommand(() => OpenFolder(BackupRoot));
        RefreshVerificationCommand = new RelayCommand(() => RefreshVerification(reInspect: true));

        ReloadProfiles();
        _ = LoadGpuAsync();
    }

    public event Action<AppSection>? NavigationRequested;

    public ObservableCollection<GameProfileViewModel> Profiles { get; } = new();
    public ObservableCollection<PayloadVersion> Versions { get; }
    public ObservableCollection<string> Routers { get; }
    public ObservableCollection<string> KernelImages { get; }
    public ObservableCollection<int> MaxFrames { get; }
    public ObservableCollection<int> LoggingLevels { get; }
    public ObservableCollection<Preset> Presets { get; }

    public RelayCommand AddGameCommand { get; }
    public RelayCommand ScanSteamCommand { get; }
    public RelayCommand RemoveGameCommand { get; }
    public RelayCommand<GameProfileViewModel> ConfigureGameCommand { get; }
    public RelayCommand ShowGamesCommand { get; }
    public RelayCommand CopyStatusCommand { get; }
    public RelayCommand OpenGameFolderCommand { get; }
    public RelayCommand OpenBackupFolderCommand { get; }
    public RelayCommand RefreshVerificationCommand { get; }

    public static string BackupRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssgSwapper", "backups");

    public string AppVersion { get; } =
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();

    public GameProfileViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;
            OnPropertyChanged(nameof(HasSelectedProfile));
            OnSelectionChanged();
            OnPropertyChanged(nameof(IsInstalled));
            RefreshVerification(reInspect: false);
            RaiseCommandStates();
        }
    }

    public bool HasSelectedProfile => _selectedProfile != null;

    public bool IsInstalled
    {
        get => _selectedProfile?.Inspection.Kind == InstallKind.Installed;
        set
        {
            if (value == IsInstalled) return;
            if (value) Install();
            else Uninstall();
            OnPropertyChanged();
        }
    }

    public PayloadVersion? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!Set(ref _selectedVersion, value)) return;
            if (value != null)
            {
                string? previous = _selectedEntryPoint?.FileName;
                _selectedEntryPoint = value.FindEntryPoint(previous ?? "")
                    ?? value.EntryPoints.FirstOrDefault(e => e.Recommended);
                OnPropertyChanged(nameof(SelectedEntryPoint));
                ApplySchemaDefaults();
            }
            RaiseCommandStates();
        }
    }

    public PayloadEntryPoint? SelectedEntryPoint
    {
        get => _selectedEntryPoint;
        set
        {
            if (!Set(ref _selectedEntryPoint, value)) return;
            RaiseCommandStates();
        }
    }

    public Preset SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!Set(ref _selectedPreset, value)) return;
            ApplyPreset();
        }
    }

    public string SelectedRouter { get => _selectedRouter; set => Set(ref _selectedRouter, value); }
    public string SelectedKernelImage { get => _selectedKernelImage; set => Set(ref _selectedKernelImage, value); }
    public int SelectedMaxFrames { get => _selectedMaxFrames; set => Set(ref _selectedMaxFrames, value); }
    public int SelectedLoggingLevel { get => _selectedLoggingLevel; set => Set(ref _selectedLoggingLevel, value); }
    public bool HardwareBilinear { get => _hardwareBilinear; set => Set(ref _hardwareBilinear, value); }
    public bool OverwriteForeign { get => _overwriteForeign; set => Set(ref _overwriteForeign, value); }

    public string GpuSummary { get => _gpuSummary; set => Set(ref _gpuSummary, value); }
    public string GpuRouterSuggestion { get => _gpuRouterSuggestion; set => Set(ref _gpuRouterSuggestion, value); }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public VerificationReport? Verification
    {
        get => _verification;
        private set => Set(ref _verification, value);
    }

    public AppTheme SelectedTheme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value) return;
            _settings.Theme = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool DefaultOverwriteForeign
    {
        get => _settings.DefaultOverwriteForeign;
        set
        {
            if (_settings.DefaultOverwriteForeign == value) return;
            _settings.DefaultOverwriteForeign = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    private FrameGenSettings CurrentSettings => new()
    {
        Router = _selectedRouter,
        KernelImage = _selectedKernelImage,
        HardwareBilinear = _hardwareBilinear ? 1 : 0,
        MaxGeneratedFrames = _selectedMaxFrames,
        LoggingLevel = _selectedLoggingLevel,
    };

    private void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _store.Profiles)
            Profiles.Add(new GameProfileViewModel(profile, SafeInspect(profile)));
        SelectedProfile = Profiles.FirstOrDefault();
    }

    private InstallInspection SafeInspect(GameProfile profile)
    {
        try
        {
            return _service.Inspect(profile);
        }
        catch (Exception e)
        {
            return new InstallInspection { Kind = InstallKind.NotInstalled, Warnings = new[] { e.Message } };
        }
    }

    private void OnSelectionChanged()
    {
        var profile = _selectedProfile?.Profile;
        if (profile == null)
        {
            SelectedVersion = Versions.FirstOrDefault();
            return;
        }

        var version = profile.PayloadVersion != null ? _catalog.GetVersion(profile.PayloadVersion) : null;
        SelectedVersion = version ?? Versions.FirstOrDefault();

        var settings = profile.Settings;
        if (settings != null)
        {
            if (settings.Router != null) SelectedRouter = settings.Router;
            if (settings.KernelImage != null) SelectedKernelImage = settings.KernelImage;
            if (settings.MaxGeneratedFrames != null) SelectedMaxFrames = settings.MaxGeneratedFrames.Value;
            if (settings.LoggingLevel != null) SelectedLoggingLevel = settings.LoggingLevel.Value;
            HardwareBilinear = settings.HardwareBilinear == 1;
        }

        if (SelectedVersion != null)
        {
            SelectedEntryPoint = profile.PayloadEntryPoint != null
                ? SelectedVersion.FindEntryPoint(profile.PayloadEntryPoint)
                : SelectedVersion.EntryPoints.FirstOrDefault(e => e.Recommended);
        }
    }

    private void ApplySchemaDefaults()
    {
        var defaults = FrameGenSettings.Defaults();
        SelectedRouter = defaults.Router ?? SelectedRouter;
        SelectedKernelImage = defaults.KernelImage ?? SelectedKernelImage;
        SelectedMaxFrames = defaults.MaxGeneratedFrames ?? SelectedMaxFrames;
        SelectedLoggingLevel = defaults.LoggingLevel ?? SelectedLoggingLevel;
        HardwareBilinear = defaults.HardwareBilinear == 1;
        ApplyPreset();
    }

    private void ApplyPreset()
    {
        if (_selectedPreset == Preset.Performance)
            HardwareBilinear = true;
    }

    private void ConfigureGame(GameProfileViewModel? profile)
    {
        if (profile != null) SelectedProfile = profile;
        if (_selectedProfile == null) return;
        NavigationRequested?.Invoke(AppSection.Install);
    }

    private void CopyStatus()
    {
        try
        {
            Clipboard.SetText(StatusText);
        }
        catch (Exception)
        {
            // Clipboard can be locked by another process; copying is best-effort.
        }
    }

    private void AddGame()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the game's rendering executable (e.g. b1-Win64-Shipping.exe)",
            Filter = "Game executable (*.exe)|*.exe|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName)) return;

        var profile = GameProfile.Create(
            $"{Path.GetFileNameWithoutExtension(dialog.FileName)} (manual)",
            dialog.FileName,
            GameSource.Manual);
        _store.Add(profile);
        SetStatus($"Added {profile.Name}.");
        ReloadProfiles();
        SelectedProfile = Profiles.Last();
    }

    private void ScanSteam()
    {
        var dialog = new SteamScanWindow { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.SelectedExe == null) return;
        string? gameName = dialog.SelectedGameName;
        if (gameName == null) return;

        var profile = GameProfile.Create(gameName, dialog.SelectedExe, GameSource.Steam);
        _store.Add(profile);
        SetStatus($"Added Steam game {profile.Name}.");
        ReloadProfiles();
        SelectedProfile = Profiles.Last();
    }

    private void RemoveGame()
    {
        var item = _selectedProfile;
        if (item == null) return;

        var result = MessageBox.Show(
            $"Remove \"{item.Name}\"?\n\nFiles already installed in the game directory are left unchanged; " +
            "use Uninstall first to restore original files. The saved backups for this game will be deleted.",
            "Remove game", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _store.Remove(item.Profile.Id);
        try
        {
            string backupRoot = BackupStore.RootFor(item.Profile.Id);
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true);
        }
        catch (Exception e)
        {
            SetStatus($"Removed profile but could not delete its backups: {e.Message}");
            ReloadProfiles();
            return;
        }
        SetStatus($"Removed {item.Name}.");
        ReloadProfiles();
    }

    private void Install()
    {
        var item = _selectedProfile;
        if (item == null || _selectedVersion == null || _selectedEntryPoint == null) return;

        try
        {
            var warnings = _service.Install(item.Profile, _selectedVersion, _selectedEntryPoint, CurrentSettings, _overwriteForeign);
            _store.Update(item.Profile with
            {
                Settings = CurrentSettings,
                PayloadVersion = _selectedVersion.Version,
                PayloadEntryPoint = _selectedEntryPoint.FileName,
            });
            string message = $"Installed {_selectedVersion.Version} via {_selectedEntryPoint.FileName} into " +
                             $"{Path.GetDirectoryName(item.ExecutablePath)}.";
            if (warnings.Count > 0) message += Environment.NewLine + string.Join(Environment.NewLine, warnings);
            SetStatus(message);
            RefreshSelected();
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
            MessageBox.Show(e.Message, "Install failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Uninstall()
    {
        var item = _selectedProfile;
        if (item == null) return;

        try
        {
            var warnings = _service.Uninstall(item.Profile);
            string message = $"Uninstalled from {Path.GetDirectoryName(item.ExecutablePath)}.";
            if (warnings.Count > 0) message += Environment.NewLine + string.Join(Environment.NewLine, warnings);
            SetStatus(message);
            RefreshSelected();
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
            MessageBox.Show(e.Message, "Uninstall failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshVerification(bool reInspect)
    {
        var item = _selectedProfile;
        if (item == null)
        {
            Verification = null;
            return;
        }

        if (reInspect)
        {
            item.Update(item.Profile, SafeInspect(item.Profile));
            OnPropertyChanged(nameof(IsInstalled));
        }

        var inspection = item.Inspection;
        var log = LogInspector.Check(item.Profile.GameDirectory);

        var lines = new List<string>
        {
            $"State: {inspection.Kind}",
            $"Proxy origin: {inspection.ProxyOrigin}",
            log.LogsFound
                ? $"Logs: {(log.Verified ? "route confirmed (install.active=true, backend status=0)" : "route markers not confirmed")}"
                : "Logs: none found yet (start the game once)",
        };
        if (log.Image != null) lines.Add($"Kernel image: {log.Image}");
        lines.AddRange(inspection.Warnings);
        lines.AddRange(log.Messages);

        string headline = !log.LogsFound ? "No logs yet" : log.Verified ? "Route confirmed" : "Route not confirmed";
        Verification = new VerificationReport(headline, lines);
    }

    private void RefreshSelected()
    {
        var item = _selectedProfile;
        if (item == null) return;
        var fresh = _store.Find(item.Profile.Id);
        if (fresh == null) return;

        item.Update(fresh, SafeInspect(fresh));
        OnPropertyChanged(nameof(IsInstalled));
        RefreshVerification(reInspect: false);
    }

    private void OpenGameFolder()
    {
        var item = _selectedProfile;
        if (item == null) return;
        OpenFolder(item.ExecutableDirectory);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening Explorer is a convenience; failure is not worth an error dialog.
        }
    }

    private void SetStatus(string text) => StatusText = text;

    private void RaiseCommandStates()
    {
        AddGameCommand.RaiseCanExecuteChanged();
        ScanSteamCommand.RaiseCanExecuteChanged();
        RemoveGameCommand.RaiseCanExecuteChanged();
        ConfigureGameCommand.RaiseCanExecuteChanged();
        OpenGameFolderCommand.RaiseCanExecuteChanged();
    }

    private async Task LoadGpuAsync()
    {
        var info = await Task.Run(GpuDetector.Detect);
        GpuSummary = info.Name != null
            ? $"{info.Name}  ·  compute {info.ComputeCapability}  ·  driver {info.DriverVersion}"
            : "GPU not detected (nvidia-smi unavailable)";
        GpuRouterSuggestion = info.SuggestedRouter != null ? $"Suggested Router: {info.SuggestedRouter}" : "";
        if (info.SuggestedRouter != null)
            SelectedRouter = info.SuggestedRouter;
    }
}
