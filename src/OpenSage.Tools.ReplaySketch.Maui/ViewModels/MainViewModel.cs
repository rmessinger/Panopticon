using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Maui.ViewModels;

/// <summary>
/// Top-level view model that owns the <see cref="ReplayScenario"/> and coordinates
/// map loading, validation, export, and launcher invocation.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // ── Services ──────────────────────────────────────────────────────────
    private readonly GeneralsInstallationService _installationService = new();
    private readonly LauncherLocatorService _launcherLocator = new();

    // ── State ─────────────────────────────────────────────────────────────
    public ReplayScenario Scenario { get; } = ReplayScenario.CreateAlpineAssaultUSAvGLA();

    private ObservableCollection<string> _availableMaps = new();
    public ObservableCollection<string> AvailableMaps
    {
        get => _availableMaps;
        private set { _availableMaps = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Incremented each time <see cref="AvailableMaps"/> is fully replaced after async discovery.
    /// Views subscribe to this via <see cref="INotifyPropertyChanged"/> to refresh the picker once.
    /// </summary>
    public int MapsGeneration { get; private set; }

    private IReadOnlyList<MapDiscoveryService.MapOption> _allMapOptions = new List<MapDiscoveryService.MapOption>();
    private bool _initialized;
    private int _mapsLoadVersion;

    private MapMetadataService? _map;
    public MapMetadataService? Map
    {
        get => _map;
        private set { _map = value; OnPropertyChanged(); OnPropertyChanged(nameof(MapLoaded)); }
    }

    private string? _mapLoadError;
    public string? MapLoadError
    {
        get => _mapLoadError;
        private set { _mapLoadError = value; OnPropertyChanged(); }
    }

    public bool MapLoaded => _map != null;

    private string? _currentInstallationPath;
    public string? CurrentInstallationPath
    {
        get => _currentInstallationPath;
        private set { _currentInstallationPath = value; OnPropertyChanged(); }
    }

    private string? _selectedActionId;
    public string? SelectedActionId
    {
        get => _selectedActionId;
        set { _selectedActionId = value; OnPropertyChanged(); }
    }

    private int _selectedSlotIndex;
    public int SelectedSlotIndex
    {
        get => _selectedSlotIndex;
        set { _selectedSlotIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedPlayer)); }
    }

    public PlayerSlotConfig SelectedPlayer =>
        _selectedSlotIndex >= 0 && _selectedSlotIndex < Scenario.Players.Count
            ? Scenario.Players[_selectedSlotIndex]
            : Scenario.Players[0];

    // ── Validation / export state ──────────────────────────────────────────
    private string? _validationResult;
    public string? ValidationResult
    {
        get => _validationResult;
        private set { _validationResult = value; OnPropertyChanged(); }
    }

    private bool _validationOk;
    public bool ValidationOk
    {
        get => _validationOk;
        private set { _validationOk = value; OnPropertyChanged(); }
    }

    private string? _exportResult;
    public string? ExportResult
    {
        get => _exportResult;
        private set { _exportResult = value; OnPropertyChanged(); }
    }

    private bool _exportOk;
    public bool ExportOk
    {
        get => _exportOk;
        private set { _exportOk = value; OnPropertyChanged(); }
    }

    public string? LauncherPath => _launcherLocator.Locate();

    // ── Commands ───────────────────────────────────────────────────────────
    private readonly RelayCommand _validateCommand;
    private readonly RelayCommand _exportCommand;
    private readonly RelayCommand _playInGameCommand;
    public ICommand ValidateCommand => _validateCommand;
    public ICommand ExportCommand => _exportCommand;
    public ICommand PlayInGameCommand => _playInGameCommand;
    public ICommand ReloadMapCommand { get; }

    public MainViewModel()
    {
        _validateCommand = new RelayCommand(
            execute: _ => ExecuteValidate(),
            canExecute: _ => MapLoaded);

        _exportCommand = new RelayCommand(
            execute: _ => ExecuteExport(),
            canExecute: _ => MapLoaded);

        _playInGameCommand = new RelayCommand(
            execute: _ => ExecutePlayInGame(),
            canExecute: _ => MapLoaded && LauncherPath != null);

        ReloadMapCommand = new RelayCommand(execute: _ => LoadMap());

        // Restore a previously saved installation path (MAUI apps don't inherit shell env vars)
        var savedPath = Preferences.Default.Get("cnc_generals_path", string.Empty);
        if (!string.IsNullOrEmpty(savedPath))
        {
            ReplaySketchLog.Write($"MainViewModel ctor restoring saved path '{savedPath}'");
            _installationService.SetPath(savedPath);
            CurrentInstallationPath = savedPath;
        }

        UpdateCommandStates();
    }

    public void Initialize()
    {
        ReplaySketchLog.Write("MainViewModel.Initialize called");

        if (_initialized)
        {
            ReplaySketchLog.Write("MainViewModel.Initialize ignored because it already ran");
            return;
        }

        _initialized = true;
        LoadMap();
    }

    /// <summary>
    /// Loads the current scenario map immediately once an installation has been resolved.
    /// Available-map discovery runs in the background so a slow scan can't block the first map.
    /// </summary>
    public void LoadMap()
    {
        ReplaySketchLog.Write($"LoadMap start map='{Scenario.MapPath}'");

        Map = null;
        MapLoadError = null;

        if (!TryResolveInstallation(out var installation) || installation is null)
        {
            var diag = _installationService.GetDiagnostics();
            MapLoadError =
                $"Generals installation not found.\nSet CNC_GENERALS_PATH to the game folder, or use Browse below.\n\nDiagnostics:\n{diag}\n\nLog: {ReplaySketchLog.LogPath}";
            ReplaySketchLog.Write($"LoadMap failed because installation was not found. Diagnostics: {diag.Replace(Environment.NewLine, " | ")}");
            UpdateCommandStates();
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Map = MapMetadataService.Load(installation, Scenario.MapPath);
            ReplaySketchLog.Write(
                $"LoadMap succeeded installation='{installation.Path}' map='{Scenario.MapPath}' elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            MapLoadError = $"{ex.Message}\n\nLog: {ReplaySketchLog.LogPath}";
            ReplaySketchLog.WriteException(
                $"LoadMap failed installation='{installation.Path}' map='{Scenario.MapPath}'", ex);
        }

        UpdateCommandStates();
        StartBackgroundMapDiscovery(installation);
    }

    public string? GetDiscoveredMapPath(int index)
    {
        if (index < 0 || index >= _allMapOptions.Count)
        {
            return null;
        }

        return _allMapOptions[index].Path;
    }

    public int GetDiscoveredMapIndex(string mapPath)
    {
        for (var i = 0; i < _allMapOptions.Count; i++)
        {
            if (string.Equals(_allMapOptions[i].Path, mapPath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public IReadOnlyList<string> GetDiscoveredMapPaths() => _allMapOptions.Select(m => m.Path).ToList();

    public void LoadMapFromFolder(string folderPath)
    {
        ReplaySketchLog.Write($"LoadMapFromFolder path='{folderPath}'");
        _installationService.SetPath(folderPath);
        Preferences.Default.Set("cnc_generals_path", folderPath);
        CurrentInstallationPath = folderPath;
        LoadMap();
    }

    private void ExecuteValidate()
    {
        if (_map == null) return;
        var result = TerrainValidator.Validate(Scenario, _map);
        ValidationOk = result.IsValid;
        ValidationResult = result.IsValid
            ? "✓ All positions valid."
            : string.Join(Environment.NewLine, result.Errors);
    }

    private void ExecuteExport()
    {
        if (_map == null) return;
        var replaysDir = ReplaysPathResolver.Resolve();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var outputPath = System.IO.Path.Combine(replaysDir, $"sketch_{timestamp}.rep");
        var error = ReplayExporter.Export(Scenario, _map, outputPath, out _);
        ExportOk = error == null;
        ExportResult = error ?? $"✓ Exported to:\n{outputPath}";
    }

    private const string PreviewReplayName = "sketch_preview.rep";

    private void ExecutePlayInGame()
    {
        if (_map == null) return;
        var launcherPath = _launcherLocator.Locate();
        if (launcherPath == null) return;

        var replaysDir = ReplaysPathResolver.Resolve();
        var outputPath = System.IO.Path.Combine(replaysDir, PreviewReplayName);
        var error = ReplayExporter.Export(Scenario, _map, outputPath, out _);
        ExportOk = error == null;
        ExportResult = error != null
            ? $"Export failed: {error}"
            : $"✓ Exported to:\n{outputPath}";

        if (error != null) return;

        var psi = new System.Diagnostics.ProcessStartInfo(launcherPath)
        {
            Arguments = $"--replay {PreviewReplayName} --noaudio --noshellmap",
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        System.Diagnostics.Process.Start(psi);
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Map) or nameof(MapLoadError))
        {
            UpdateCommandStates();
        }
    }

    private void UpdateCommandStates()
    {
        _validateCommand.RaiseCanExecuteChanged();
        _exportCommand.RaiseCanExecuteChanged();
        _playInGameCommand.RaiseCanExecuteChanged();
    }

    private bool TryResolveInstallation(out OpenSage.Data.GameInstallation? installation)
    {
        var foundInstallation = _installationService.TryGetInstallation(out installation) && installation is not null;
        CurrentInstallationPath = installation?.Path;

        if (foundInstallation)
        {
            ReplaySketchLog.Write($"TryResolveInstallation resolved '{installation!.Path}'");
        }
        else
        {
            ReplaySketchLog.Write("TryResolveInstallation did not resolve an installation");
        }

        return foundInstallation;
    }

    private void StartBackgroundMapDiscovery(OpenSage.Data.GameInstallation installation)
    {
        var version = Interlocked.Increment(ref _mapsLoadVersion);
        var installationPath = installation.Path;

        ReplaySketchLog.Write($"StartBackgroundMapDiscovery start installation='{installationPath}' version={version}");

        Task.Run(async () =>
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var maps = MapDiscoveryService.DiscoverMaps(installation);
                ReplaySketchLog.Write(
                    $"StartBackgroundMapDiscovery discovered {maps.Count} maps from '{installationPath}' in {stopwatch.ElapsedMilliseconds} ms");

                if (version != _mapsLoadVersion)
                {
                    ReplaySketchLog.Write(
                        $"StartBackgroundMapDiscovery discarded stale result for installation='{installationPath}' version={version}");
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _allMapOptions = maps;
                    AvailableMaps = new ObservableCollection<string>(maps.Select(m => m.Label));
                    MapsGeneration++;
                    OnPropertyChanged(nameof(MapsGeneration));
                });
            }
            catch (Exception ex)
            {
                ReplaySketchLog.WriteException(
                    $"StartBackgroundMapDiscovery failed installation='{installationPath}'", ex);
            }
        });
    }
}

/// <summary>
/// Minimal ICommand implementation backed by delegates.
/// </summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
