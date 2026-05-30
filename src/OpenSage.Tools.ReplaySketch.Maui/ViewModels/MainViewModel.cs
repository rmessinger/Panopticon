using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    public ICommand ValidateCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand PlayInGameCommand { get; }
    public ICommand ReloadMapCommand { get; }

    public MainViewModel()
    {
        ValidateCommand = new RelayCommand(
            execute: _ => ExecuteValidate(),
            canExecute: _ => MapLoaded);

        ExportCommand = new RelayCommand(
            execute: _ => ExecuteExport(),
            canExecute: _ => MapLoaded);

        PlayInGameCommand = new RelayCommand(
            execute: _ => ExecutePlayInGame(),
            canExecute: _ => MapLoaded && LauncherPath != null);

        ReloadMapCommand = new RelayCommand(execute: _ => LoadMap());

        // Restore a previously saved installation path (MAUI apps don't inherit shell env vars)
        var savedPath = Preferences.Default.Get("cnc_generals_path", string.Empty);
        if (!string.IsNullOrEmpty(savedPath))
            _installationService.SetPath(savedPath);

        LoadMap();
    }

    public void LoadMap()
    {
        Map = null;
        MapLoadError = null;

        if (!_installationService.TryGetInstallation(out var installation) || installation is null)
        {
            MapLoadError = "Generals installation not found.\nSet CNC_GENERALS_PATH to the game folder, or use Browse below.";
            return;
        }

        try
        {
            Map = MapMetadataService.Load(installation, Scenario.MapPath);
        }
        catch (Exception ex)
        {
            MapLoadError = ex.Message;
        }
    }

    public void LoadMapFromFolder(string folderPath)
    {
        _installationService.SetPath(folderPath);
        LoadMap();
        if (MapLoaded)
            Preferences.Default.Set("cnc_generals_path", folderPath);
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
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
