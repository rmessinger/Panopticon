using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.ViewModels;

public sealed class HarnessLauncherViewModel : INotifyPropertyChanged
{
    private readonly IHarnessCatalogClient _catalogClient;
    private readonly IHarnessRunClient _runClient;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly HarnessPresetStore _presetStore;
    private HarnessCatalog? _catalog;
    private string _filterText = string.Empty;
    private HarnessScenarioCategory? _categoryFilter;
    private HarnessRunModes _modeFilter;
    private HarnessScenarioDescriptor? _selectedScenario;
    private string? _statusMessage;
    private HarnessRunResult? _lastResult;
    private readonly List<string> _output = [];

    public HarnessLauncherViewModel(IHarnessCatalogClient catalogClient, IHarnessRunClient runClient, IMainThreadDispatcher dispatcher, HarnessPresetStore? presetStore = null)
    {
        _catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
        _runClient = runClient ?? throw new ArgumentNullException(nameof(runClient));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _presetStore = presetStore ?? new HarnessPresetStore();
        Parameters = new HarnessParameterEditorViewModel();
        LoadCatalogCommand = new AsyncCommand(LoadCatalogAsync);
        RunCommand = new AsyncCommand(RunAsync, CanRun);
        Parameters.PropertyChanged += (_, _) => { OnChanged(nameof(HasErrors)); RunCommand.RaiseCanExecuteChanged(); };
        StopCommand = new AsyncCommand(_ => { RunCommand.Cancel(); return Task.CompletedTask; }, () => RunCommand.IsExecuting);
        RunCommand.CanExecuteChanged += (_, _) => StopCommand.RaiseCanExecuteChanged();
        RunCommand.UnhandledException += (_, error) => _ = _dispatcher.InvokeOnMainThreadAsync(() => StatusMessage = error.Message);
        LoadCatalogCommand.UnhandledException += (_, error) => _ = _dispatcher.InvokeOnMainThreadAsync(() => StatusMessage = error.Message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public AsyncCommand LoadCatalogCommand { get; }

    public AsyncCommand RunCommand { get; }

    public AsyncCommand StopCommand { get; }

    public HarnessParameterEditorViewModel Parameters { get; }

    public IReadOnlyList<HarnessScenarioDescriptor> AllScenarios { get; private set; } = [];
    public IReadOnlyList<HarnessScenarioDescriptor> VisibleScenarios { get; private set; } = [];
    public IReadOnlyList<string> Output => _output;
    public IReadOnlyList<string> Presets => _presetStore.List();
    public bool HasErrors => Parameters.HasErrors;
    public HarnessRunResult? LastResult { get => _lastResult; private set { _lastResult = value; OnChanged(nameof(LastResult)); } }

    public string? StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnChanged(nameof(StatusMessage)); } }

    public string FilterText { get => _filterText; set { _filterText = value ?? string.Empty; RefreshVisible(); } }

    public HarnessScenarioCategory? CategoryFilter { get => _categoryFilter; set { _categoryFilter = value; RefreshVisible(); } }

    public HarnessRunModes ModeFilter { get => _modeFilter; set { _modeFilter = value; RefreshVisible(); } }

    public HarnessScenarioDescriptor? SelectedScenario { get => _selectedScenario; set { if (_selectedScenario == value) return; _selectedScenario = value; if (value is not null && _catalog is not null) Parameters.Load(_catalog, value); OnChanged(nameof(SelectedScenario)); RunCommand.RaiseCanExecuteChanged(); } }

    public void SavePreset(string name) { if (SelectedScenario is null) return; _presetStore.Save(name, Parameters.BuildRequest(SelectedScenario.Id)); OnChanged(nameof(Presets)); }

    public void DeletePreset(string name) { _presetStore.Delete(name); OnChanged(nameof(Presets)); }

    public void LoadPreset(string name)
    {
        if (_catalog is null) return;
        var preset = _presetStore.Load(name); var scenario = AllScenarios.FirstOrDefault(item => item.Id == preset.ScenarioId);
        if (scenario is null) { StatusMessage = $"Preset scenario '{preset.ScenarioId}' is unavailable."; return; }
        FilterText = string.Empty; CategoryFilter = null; ModeFilter = HarnessRunModes.None; SelectedScenario = scenario;
        var stale = preset.Overrides.Keys.Where(key => _catalog.Parameters.All(parameter => parameter.Name != key)).ToArray();
        Parameters.ApplyOverrides(preset.Overrides.Where(pair => !stale.Contains(pair.Key)).ToDictionary());
        StatusMessage = stale.Length == 0 ? null : $"Dropped unavailable parameters: {string.Join(", ", stale)}.";
    }

    public void ImportConfig(string path)
    {
        if (_catalog is null) return;
        var imported = _presetStore.ImportConfig(path, _catalog);
        if (imported.Request is null) { StatusMessage = "Import requires a Scenario value."; return; }
        var scenario = AllScenarios.FirstOrDefault(item => item.Id == imported.Request.ScenarioId);
        if (scenario is null) { StatusMessage = $"Scenario '{imported.Request.ScenarioId}' is unavailable."; return; }
        FilterText = string.Empty; CategoryFilter = null; ModeFilter = HarnessRunModes.None;
        SelectedScenario = scenario; Parameters.ApplyOverrides(imported.Request.Overrides);
        StatusMessage = imported.UnknownKeys.Count == 0 ? null : $"Ignored unknown parameters: {string.Join(", ", imported.UnknownKeys)}.";
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var result = await _catalogClient.LoadAsync(cancellationToken).ConfigureAwait(true);
        if (!result.Succeeded) { _catalog = null; AllScenarios = []; VisibleScenarios = []; SelectedScenario = null; StatusMessage = $"{result.ErrorCode}: {result.Message}"; OnChanged(null); return; }
        _catalog = result.Catalog; AllScenarios = _catalog!.Scenarios.OrderBy(item => item.Category).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray(); StatusMessage = null; RefreshVisible();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_catalog is null || SelectedScenario is null) return;
        _output.Clear(); OnChanged(nameof(Output)); LastResult = null; StatusMessage = null;
        var progress = new Progress<string>(line => _ = _dispatcher.InvokeOnMainThreadAsync(() => { _output.Add(line); if (_output.Count > 5000) _output.RemoveRange(0, _output.Count - 5000); OnChanged(nameof(Output)); }));
        LastResult = await _runClient.RunAsync(Parameters.BuildRequest(SelectedScenario.Id), _catalog, progress, cancellationToken).ConfigureAwait(true);
    }

    private bool CanRun() => _catalog is not null && SelectedScenario is not null && !Parameters.HasErrors && !RunCommand.IsExecuting;
    private void RefreshVisible()
    {
        VisibleScenarios = AllScenarios.Where(item => (string.IsNullOrWhiteSpace(FilterText) || item.Id.Contains(FilterText, StringComparison.OrdinalIgnoreCase) || item.Summary.Contains(FilterText, StringComparison.OrdinalIgnoreCase)) && (!CategoryFilter.HasValue || item.Category == CategoryFilter) && (ModeFilter == HarnessRunModes.None || item.Modes.HasFlag(ModeFilter))).OrderBy(item => item.Category).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (SelectedScenario is not null && !VisibleScenarios.Contains(SelectedScenario)) SelectedScenario = null;
        OnChanged(nameof(VisibleScenarios));
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
