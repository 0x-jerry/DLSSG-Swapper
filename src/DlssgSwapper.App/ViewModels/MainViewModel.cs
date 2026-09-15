using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using DlssgSwapper.App.Configuration;
using DlssgSwapper.App.Mvvm;
using DlssgSwapper.App.Views;
using DlssgSwapper.Core.Configuration;
using DlssgSwapper.Core.Diagnostics;
using DlssgSwapper.Core.Games;
using DlssgSwapper.Core.Hardware;
using DlssgSwapper.Core.Payloads;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DlssgSwapper.App.ViewModels;

public enum VerificationSeverity
{
    Neutral,
    Success,
    Caution,
}

public sealed record VerificationReport(string Headline, IReadOnlyList<string> Lines, VerificationSeverity Severity = VerificationSeverity.Neutral);

public enum AppSection
{
    Games,
    Install,
    Settings,
    About,
}

public sealed class GameProfileViewModel : ObservableObject
{
    public GameProfileViewModel(
        GameProfile profile,
        InstallInspection inspection,
        Action<GameProfileViewModel> configure,
        Func<GameProfileViewModel, Task> remove)
    {
        Profile = profile;
        Inspection = inspection;
        ConfigureCommand = new RelayCommand(() => configure(this));
        RemoveCommand = new AsyncRelayCommand(() => remove(this));
    }

    public GameProfile Profile { get; set; }
    public InstallInspection Inspection { get; set; }

    public RelayCommand ConfigureCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }

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
    public InstallKind InstallKind => Inspection.Kind;

    public string InstallState => Inspection.Kind switch
    {
        InstallKind.Installed => "Installed",
        InstallKind.Partial => "Partial",
        _ => "Not installed",
    };

    public string InstallSummary => Inspection.Kind switch
    {
        InstallKind.Installed when Inspection.OutdatedProxy =>
            $"Outdated proxy {Inspection.InstalledEntryPoint?.FileName ?? ""} from a previous release - reinstall to update",
        InstallKind.Installed =>
            $"Installed {Inspection.InstalledVersion?.Version ?? "?"} via {Inspection.InstalledEntryPoint?.FileName ?? "?"}",
        InstallKind.Partial => "Partial install (proxy or INI missing)",
        _ => "Not installed",
    };
}

public sealed record EntryPointOption(PayloadEntryPoint EntryPoint, bool IsPresent)
{
    public string DisplayName => EntryPoint.FileName;

    public string Note => IsPresent
        ? "A file with this name already exists in the game folder"
        : EntryPoint.Note ?? "";
}

public sealed class MainViewModel : ObservableObject
{
    private readonly ProfileStore _store = ProfileStore.OpenDefault();
    private readonly PayloadCatalog _catalog;
    private readonly InstallationService _service;
    private readonly AppSettings _settings = AppSettings.Load();

    private GameProfileViewModel? _selectedProfile;
    private PayloadVersion? _selectedVersion;
    private EntryPointOption? _selectedEntryPoint;
    private bool _enabled = true;
    private bool _optimized = true;
    private string _selectedRouter = "Auto";
    private string _selectedKernelImage = "Auto";
    private string _selectedPreset = "Auto";
    private int _selectedMaxFrames = 3;
    private int _selectedLoggingLevel = 1;
    private bool _overwriteForeign;
    private string _gpuSummary = "Detecting GPU…";
    private string _gpuRouterSuggestion = "";
    private string _gpuWarning = "";
    private bool _gpuSupported = true;
    private string _statusText = "Add or select a game to begin.";
    private string _entryPointHint = "";
    private VerificationReport? _verification;

    public MainViewModel()
    {
        _catalog = PayloadCatalog.Load(Path.Combine(AppContext.BaseDirectory, "payloads"));
        _service = new InstallationService(_catalog);
        _overwriteForeign = _settings.DefaultOverwriteForeign;

        Versions = new ObservableCollection<PayloadVersion>(_catalog.Versions);
        Routers = new ObservableCollection<string> { "Auto", "SM86", "SM75" };
        KernelImages = new ObservableCollection<string> { "Auto", "Cubin", "PTX", "Original" };
        MaxFrames = new ObservableCollection<int>();
        LoggingLevels = new ObservableCollection<int> { 0, 1, 2, 3 };
        Presets = new ObservableCollection<string> { "Auto", "A", "B" };

        AddGameCommand = new AsyncRelayCommand(AddGame);
        ScanSteamCommand = new AsyncRelayCommand(ScanSteam);
        ShowGamesCommand = new RelayCommand(() => NavigationRequested?.Invoke(AppSection.Games));
        BackCommand = new RelayCommand(() => BackRequested?.Invoke());
        CopyStatusCommand = new RelayCommand(CopyStatus);
        OpenGameFolderCommand = new RelayCommand(OpenGameFolder, () => SelectedProfile != null);
        OpenBackupFolderCommand = new RelayCommand(() => OpenFolder(BackupRoot));
        RefreshVerificationCommand = new RelayCommand(() => RefreshVerification(reInspect: true));

        ReloadProfiles();
        _ = LoadGpuAsync();
    }

    public event Action<AppSection>? NavigationRequested;
    public event Action? BackRequested;

    public ObservableCollection<GameProfileViewModel> Profiles { get; } = new();
    public ObservableCollection<PayloadVersion> Versions { get; }
    public ObservableCollection<string> Routers { get; }
    public ObservableCollection<string> KernelImages { get; }
    public ObservableCollection<int> MaxFrames { get; }
    public ObservableCollection<int> LoggingLevels { get; }
    public ObservableCollection<string> Presets { get; }
    public ObservableCollection<EntryPointOption> EntryPointOptions { get; } = new();

    public AsyncRelayCommand AddGameCommand { get; }
    public AsyncRelayCommand ScanSteamCommand { get; }
    public RelayCommand ShowGamesCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand CopyStatusCommand { get; }
    public RelayCommand OpenGameFolderCommand { get; }
    public RelayCommand OpenBackupFolderCommand { get; }
    public RelayCommand RefreshVerificationCommand { get; }

    public string BackupRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssgSwapper", "backups");

    public string AppVersion { get; } =
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public string AboutSubtitle =>
        $"Version {AppVersion} · a manager for the dlssg_for_sm86 DLSS Frame Generation mod";

    public IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();

    public bool HasProfiles => Profiles.Count > 0;

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
            else if (_selectedProfile != null) Uninstall(_selectedProfile);
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
                RebuildMaxFrames(value.MaxGeneratedFrames);
                RebuildEntryPoints(preferredFileName: null);
            }
            RaiseCommandStates();
        }
    }

    public EntryPointOption? SelectedEntryPoint
    {
        get => _selectedEntryPoint;
        set
        {
            if (!Set(ref _selectedEntryPoint, value)) return;
            RaiseCommandStates();
        }
    }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool Optimized { get => _optimized; set => Set(ref _optimized, value); }
    public string SelectedRouter { get => _selectedRouter; set => Set(ref _selectedRouter, value); }
    public string SelectedKernelImage { get => _selectedKernelImage; set => Set(ref _selectedKernelImage, value); }
    public string SelectedPreset { get => _selectedPreset; set => Set(ref _selectedPreset, value); }
    public int SelectedMaxFrames { get => _selectedMaxFrames; set => Set(ref _selectedMaxFrames, value); }
    public int SelectedLoggingLevel { get => _selectedLoggingLevel; set => Set(ref _selectedLoggingLevel, value); }
    public bool OverwriteForeign { get => _overwriteForeign; set => Set(ref _overwriteForeign, value); }

    public string GpuSummary { get => _gpuSummary; set => Set(ref _gpuSummary, value); }
    public string GpuRouterSuggestion { get => _gpuRouterSuggestion; set => Set(ref _gpuRouterSuggestion, value); }

    public string GpuWarning
    {
        get => _gpuWarning;
        private set
        {
            if (Set(ref _gpuWarning, value)) OnPropertyChanged(nameof(HasGpuWarning));
        }
    }

    public bool GpuSupported { get => _gpuSupported; private set => Set(ref _gpuSupported, value); }

    public bool HasGpuWarning => _gpuWarning.Length > 0;

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public string EntryPointHint { get => _entryPointHint; private set => Set(ref _entryPointHint, value); }

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
        Enabled = _enabled ? 1 : 0,
        Optimized = _optimized ? 1 : 0,
        Router = _selectedRouter,
        KernelImage = _selectedKernelImage,
        Preset = _selectedPreset,
        MaxGeneratedFrames = _selectedMaxFrames,
        LoggingLevel = _selectedLoggingLevel,
    };

    private void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _store.Profiles)
            Profiles.Add(new GameProfileViewModel(profile, SafeInspect(profile), ConfigureGame, RemoveGame));
        OnPropertyChanged(nameof(HasProfiles));
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
            RebuildEntryPoints(preferredFileName: null);
            return;
        }

        var version = profile.PayloadVersion != null ? _catalog.GetVersion(profile.PayloadVersion) : null;
        SelectedVersion = version ?? Versions.FirstOrDefault();

        var settings = profile.Settings;
        if (settings != null)
        {
            if (settings.Enabled != null) Enabled = settings.Enabled == 1;
            if (settings.Optimized != null) Optimized = settings.Optimized == 1;
            if (settings.Router != null) SelectedRouter = settings.Router;
            if (settings.KernelImage != null) SelectedKernelImage = settings.KernelImage;
            if (settings.Preset != null) SelectedPreset = settings.Preset;
            if (settings.MaxGeneratedFrames != null) SelectedMaxFrames = settings.MaxGeneratedFrames.Value;
            if (settings.LoggingLevel != null) SelectedLoggingLevel = settings.LoggingLevel.Value;
        }

        if (SelectedVersion != null)
            RebuildEntryPoints(profile.PayloadEntryPoint);
    }

    private void RebuildMaxFrames(int ceiling)
    {
        MaxFrames.Clear();
        for (int frames = 0; frames <= ceiling; frames++)
            MaxFrames.Add(frames);
        if (!MaxFrames.Contains(_selectedMaxFrames))
            SelectedMaxFrames = ceiling;
    }

    private void RebuildEntryPoints(string? preferredFileName)
    {
        EntryPointOptions.Clear();
        var version = _selectedVersion;
        if (version == null)
        {
            SelectedEntryPoint = null;
            EntryPointHint = "";
            return;
        }

        string? gameDirectory = _selectedProfile == null
            ? null
            : Path.GetDirectoryName(_selectedProfile.Profile.ExecutablePath);
        var present = EntryPointDetector.FindPresent(
            gameDirectory, version.EntryPoints.Select(e => e.FileName));
        foreach (var entryPoint in version.EntryPoints)
            EntryPointOptions.Add(new EntryPointOption(entryPoint, present.Contains(entryPoint.FileName)));

        SelectedEntryPoint =
            EntryPointOptions.FirstOrDefault(o => NameEq(o, preferredFileName))
            ?? EntryPointOptions.FirstOrDefault(o => NameEq(o, _selectedEntryPoint?.EntryPoint.FileName))
            ?? EntryPointOptions.FirstOrDefault(o => o.EntryPoint.Recommended)
            ?? EntryPointOptions.FirstOrDefault();

        string recommended = version.EntryPoints.FirstOrDefault(e => e.Recommended)?.FileName ?? "version.dll";
        EntryPointHint = present.Count > 0
            ? $"Found in the game folder: {string.Join(", ", present)}. Marked entries already exist next to the executable."
            : $"No proxy DLL from the payload was found in the game folder; the recommended {recommended} is selected by default.";
    }

    private static bool NameEq(EntryPointOption option, string? fileName) =>
        fileName != null && string.Equals(option.EntryPoint.FileName, fileName, StringComparison.OrdinalIgnoreCase);

    private void ConfigureGame(GameProfileViewModel profile)
    {
        SelectedProfile = profile;
        if (_selectedProfile == null) return;
        NavigationRequested?.Invoke(AppSection.Install);
    }

    private void CopyStatus()
    {
        try
        {
            var package = new DataPackage();
            package.SetText(StatusText);
            Clipboard.SetContent(package);
        }
        catch (Exception)
        {
            // Clipboard can be locked by another process; copying is best-effort.
        }
    }

    private async Task AddGame()
    {
        if (App.MainWindow == null) return;

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var profile = GameProfile.Create(
                $"{Path.GetFileNameWithoutExtension(file.Path)} (manual)",
                file.Path,
                GameSource.Manual);
            _store.Add(profile);
            SetStatus($"Added {profile.Name}.");
            ReloadProfiles();
            SelectedProfile = Profiles.Last();
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
            Dialogs.Error("Add game failed", e.Message);
        }
    }

    private async Task ScanSteam()
    {
        var xamlRoot = (App.MainWindow?.Content as FrameworkElement)?.XamlRoot;
        if (xamlRoot == null) return;

        try
        {
            var dialog = new SteamScanDialog { XamlRoot = xamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.SelectedExe == null) return;
            if (dialog.SelectedGameName == null) return;

            var profile = GameProfile.Create(dialog.SelectedGameName, dialog.SelectedExe, GameSource.Steam);
            _store.Add(profile);
            SetStatus($"Added Steam game {profile.Name}.");
            ReloadProfiles();
            SelectedProfile = Profiles.Last();
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
            Dialogs.Error("Add game from Steam failed", e.Message);
        }
    }

    private async Task RemoveGame(GameProfileViewModel item)
    {
        try
        {
            bool installed = item.Inspection.Kind != InstallKind.NotInstalled;
            var choice = await Dialogs.ConfirmDestructiveAsync(
                "Remove game",
                $"“{item.Name}” will be removed from the library, and its saved backups will be deleted.",
                "Remove",
                optionText: installed ? "Uninstall the installed files first (restores the originals)" : null,
                optionDefault: true);
            if (!choice.Confirmed) return;

            if (choice.OptionChecked && !Uninstall(item)) return;

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
            SetStatus(choice.OptionChecked
                ? $"Removed {item.Name} and uninstalled its files."
                : $"Removed {item.Name}.");
            ReloadProfiles();
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
            Dialogs.Error("Remove game failed", e.Message);
        }
    }

    private void Install()
    {
        var item = _selectedProfile;
        if (item == null || _selectedVersion == null || _selectedEntryPoint == null) return;

        if (!GpuSupported)
        {
            SetStatus(GpuWarning);
            Dialogs.Error("Unsupported GPU", GpuWarning);
            return;
        }

        try
        {
            var entryPoint = _selectedEntryPoint.EntryPoint;
            var warnings = _service.Install(item.Profile, _selectedVersion, entryPoint, CurrentSettings, _overwriteForeign);
            _store.Update(item.Profile with
            {
                Settings = CurrentSettings,
                PayloadVersion = _selectedVersion.Version,
                PayloadEntryPoint = entryPoint.FileName,
            });
            string message = $"Installed {_selectedVersion.Version} via {entryPoint.FileName} into " +
                             $"{Path.GetDirectoryName(item.ExecutablePath)}.";
            if (warnings.Count > 0) message += Environment.NewLine + string.Join(Environment.NewLine, warnings);
            SetStatus(message);
            RefreshSelected();
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
            Dialogs.Error("Install failed", e.Message);
        }
    }

    private bool Uninstall(GameProfileViewModel item)
    {
        try
        {
            var warnings = _service.Uninstall(item.Profile);
            string message = $"Uninstalled from {Path.GetDirectoryName(item.ExecutablePath)}.";
            if (warnings.Count > 0) message += Environment.NewLine + string.Join(Environment.NewLine, warnings);
            SetStatus(message);
            item.Update(item.Profile, SafeInspect(item.Profile));
            OnPropertyChanged(nameof(IsInstalled));
            RefreshVerification(reInspect: false);
            return true;
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
            Dialogs.Error("Uninstall failed", e.Message);
            return false;
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

        var severity = !log.LogsFound
            ? VerificationSeverity.Neutral
            : log.Verified ? VerificationSeverity.Success : VerificationSeverity.Caution;
        string headline = !log.LogsFound ? "No logs yet" : log.Verified ? "Route confirmed" : "Route not confirmed";
        Verification = new VerificationReport(headline, lines, severity);
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

        GpuSupported = info.IsSupported;
        GpuWarning = info.IsSupported
            ? ""
            : $"Only RTX 30 (SM86) and RTX 20 (SM75) series are supported. This machine reports {info.Describe()}.";
    }
}
