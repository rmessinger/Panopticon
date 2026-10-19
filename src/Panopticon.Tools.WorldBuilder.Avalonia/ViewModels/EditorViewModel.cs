using System.ComponentModel;
using System.Runtime.CompilerServices;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Tools.WorldBuilder.Avalonia.ViewModels;

/// <summary>
/// Root view model for the editor. Holds active map, tool selection, undo/redo
/// state, and view-toggle booleans consumed by the viewport renderer.
/// </summary>
public sealed class EditorViewModel : INotifyPropertyChanged
{
    private EditorMap? _currentMap;
    private IEditorTool? _activeTool;
    private ScenarioPopulationDocument? _scenarioPopulation;
    private string _statusMessage = "Ready";
    private bool _canUndo;
    private bool _canRedo;

    // View toggles (bound to View menu items)
    private bool _showGrid = true;
    private bool _showObjects = true;
    private bool _showWater = true;
    private bool _showWaypoints = true;
    private bool _isLockAngle;
    private bool _isTopDown;
    private bool _showPassability;

    // ── Tool instances ────────────────────────────────────────────────────────
    public SelectTool SelectTool { get; } = new SelectTool();
    public HeightBrushTool MoundTool { get; } = new HeightBrushTool { Mode = HeightBrushMode.Mound };
    public HeightBrushTool DigTool { get; } = new HeightBrushTool { Mode = HeightBrushMode.Dig };
    public HeightBrushTool SmoothTool { get; } = new HeightBrushTool { Mode = HeightBrushMode.Smooth };
    public MeshMoldTool MeshMoldTool { get; } = new MeshMoldTool();
    public PaintSingleTileTool PaintTileTool { get; } = new PaintSingleTileTool();
    public PaintLargeTileTool PaintLargeTileTool { get; } = new PaintLargeTileTool();
    public SpillPaintTool SpillPaintTool { get; } = new SpillPaintTool();
    public EyeDropperTool EyeDropperTool { get; } = new EyeDropperTool();
    public PlaceObjectTool PlaceObjectTool { get; } = new PlaceObjectTool();
    public WaypointTool WaypointTool { get; } = new WaypointTool();

    public DeleteTool DeleteTool { get; }

    public EditorViewModel()
    {
        DeleteTool = new DeleteTool(SelectTool);
    }

    public EditorMap? CurrentMap
    {
        get => _currentMap;
        set
        {
            if (_currentMap?.UndoStack is { } oldStack)
            {
                oldStack.StateChanged -= OnUndoStackStateChanged;
            }

            SetField(ref _currentMap, value);
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(HasMap));

            if (_currentMap?.UndoStack is { } newStack)
            {
                newStack.StateChanged += OnUndoStackStateChanged;
            }

            // WI-A5-30: the population companion is per-map; a new map invalidates it.
            _scenarioPopulation = null;
            OnPropertyChanged(nameof(ScenarioPopulation));

            RefreshUndoState();
        }
    }

    /// <summary>WI-A5-30: scenario-population companion for the current map, if opened.</summary>
    public ScenarioPopulationDocument? ScenarioPopulation => _scenarioPopulation;

    public void EnsureScenarioPopulation()
    {
        if (_currentMap is not null && _scenarioPopulation is null)
        {
            _scenarioPopulation = ScenarioPopulationDocument.Open(_currentMap);
            OnPropertyChanged(nameof(ScenarioPopulation));
        }
    }

    public void SaveScenarioPopulation()
    {
        if (_scenarioPopulation is { } document)
        {
            document.Save();
            StatusMessage = $"Scenario population saved ({document.Editor.Placements.Count} placements).";
        }
    }

    public bool HasMap => _currentMap is not null;

    public string Title => _currentMap?.FilePath is { } path
        ? $"Panopticon World Builder — {Path.GetFileName(path)}{(_currentMap.IsDirty ? " *" : "")}"
        : "Panopticon World Builder";

    public IEditorTool? ActiveTool
    {
        get => _activeTool;
        set => SetField(ref _activeTool, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool CanUndo
    {
        get => _canUndo;
        private set => SetField(ref _canUndo, value);
    }

    public bool CanRedo
    {
        get => _canRedo;
        private set => SetField(ref _canRedo, value);
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set => SetField(ref _showGrid, value);
    }

    public bool ShowObjects
    {
        get => _showObjects;
        set => SetField(ref _showObjects, value);
    }

    public bool ShowWater
    {
        get => _showWater;
        set => SetField(ref _showWater, value);
    }

    public bool ShowWaypoints
    {
        get => _showWaypoints;
        set => SetField(ref _showWaypoints, value);
    }

    public bool IsLockAngle
    {
        get => _isLockAngle;
        set => SetField(ref _isLockAngle, value);
    }

    public bool IsTopDown
    {
        get => _isTopDown;
        set => SetField(ref _isTopDown, value);
    }

    public bool ShowPassability
    {
        get => _showPassability;
        set => SetField(ref _showPassability, value);
    }

    public void Undo()
    {
        _currentMap?.UndoStack.Undo();
        _currentMap?.MarkDirty();
        OnPropertyChanged(nameof(Title));
    }

    public void Redo()
    {
        _currentMap?.UndoStack.Redo();
        _currentMap?.MarkDirty();
        OnPropertyChanged(nameof(Title));
    }

    public void DeleteSelected()
    {
        if (_currentMap is not null)
        {
            DeleteTool.DeleteSelected(_currentMap);
        }
    }

    private void OnUndoStackStateChanged(object? sender, EventArgs e) => RefreshUndoState();

    private void RefreshUndoState()
    {
        CanUndo = _currentMap?.UndoStack.CanUndo ?? false;
        CanRedo = _currentMap?.UndoStack.CanRedo ?? false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
