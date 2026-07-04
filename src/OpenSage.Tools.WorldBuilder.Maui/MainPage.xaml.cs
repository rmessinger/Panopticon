using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Maui.Storage;
using OpenSage.Data;
using OpenSage.Data.Map;
using OpenSage.Mods.Generals;
using OpenSage.Tools.WorldBuilder.Core;
using OpenSage.Tools.WorldBuilder.Maui.ViewModels;
using OpenSage.Tools.WorldBuilder.Maui.Views;

namespace OpenSage.Tools.WorldBuilder.Maui;

public partial class MainPage : ContentPage
{
    private readonly EditorViewModel _viewModel = new();
    private readonly GameViewportRenderer _viewportRenderer;

    public MainPage()
    {
        InitializeComponent();

        GameInstallation? installation = GameInstallation.FindAll(new[] { GeneralsDefinition.Instance }).FirstOrDefault();
        _viewportRenderer = new GameViewportRenderer
        {
            Installation = installation,
        };

        ViewportContainer.Content = new VeldridViewport
        {
            Renderer = _viewportRenderer,
        };

        BindingContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;

        if (installation == null)
        {
            _viewModel.StatusMessage = "No supported SAGE game installation found.";
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.StatusMessage))
        {
            StatusLabel.Text = _viewModel.StatusMessage;
        }
        else if (e.PropertyName is nameof(EditorViewModel.Title))
        {
            Title = _viewModel.Title;
        }
        else if (e.PropertyName is nameof(EditorViewModel.CurrentMap) && _viewModel.CurrentMap != null)
        {
            _viewportRenderer.LoadMap(_viewModel.CurrentMap);
        }
        else if (e.PropertyName is nameof(EditorViewModel.ActiveTool))
        {
            _viewportRenderer.ActiveTool = _viewModel.ActiveTool;
            _viewModel.StatusMessage = _viewModel.ActiveTool?.Name ?? "Ready";
        }
        else if (e.PropertyName is nameof(EditorViewModel.ShowGrid))
        {
            _viewportRenderer.ShowGrid = _viewModel.ShowGrid;
        }
        else if (e.PropertyName is nameof(EditorViewModel.ShowObjects))
        {
            _viewportRenderer.ShowObjects = _viewModel.ShowObjects;
        }
        else if (e.PropertyName is nameof(EditorViewModel.ShowWater))
        {
            _viewportRenderer.ShowWater = _viewModel.ShowWater;
        }
        else if (e.PropertyName is nameof(EditorViewModel.ShowWaypoints))
        {
            _viewportRenderer.ShowWaypoints = _viewModel.ShowWaypoints;
        }
        else if (e.PropertyName is nameof(EditorViewModel.ShowPassability))
        {
            _viewportRenderer.ShowPassability = _viewModel.ShowPassability;
        }
        else if (e.PropertyName is nameof(EditorViewModel.IsLockAngle))
        {
            _viewportRenderer.IsLockAngle = _viewModel.IsLockAngle;
        }
        else if (e.PropertyName is nameof(EditorViewModel.IsTopDown))
        {
            _viewportRenderer.IsTopDown = _viewModel.IsTopDown;
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        Unloaded -= OnUnloaded;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewportRenderer.Dispose();
    }

    // ── File menu ──────────────────────────────────────────────────────────────

    private async void OnMenuNew(object? sender, EventArgs e)
    {
        if (!await PromptForUnsavedChangesAsync())
        {
            return;
        }

        var wizard = new NewMapWizardPage();
        wizard.MapCreated += OnNewMapCreated;
        await Navigation.PushModalAsync(wizard);
    }

    private void OnNewMapCreated(object? sender, EditorMap map)
    {
        _viewModel.CurrentMap = map;
        _viewModel.StatusMessage = "New map created.";
    }

    private async void OnMenuOpen(object? sender, EventArgs e)
    {
        if (!await PromptForUnsavedChangesAsync())
        {
            return;
        }

        var options = new PickOptions
        {
            PickerTitle = "Open Map File",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".map"] },
                { DevicePlatform.MacCatalyst, ["public.data"] },
            }),
        };

        var result = await FilePicker.Default.PickAsync(options);
        if (result is null)
        {
            return;
        }

        try
        {
            using var stream = await result.OpenReadAsync();
            var mapFile = MapFile.FromStream(stream);
            var editorMap = EditorMap.Open(mapFile, result.FullPath);
            _viewModel.CurrentMap = editorMap;
            _viewModel.StatusMessage = $"Opened: {Path.GetFileName(result.FullPath)}";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Open Error", ex.Message, "OK");
        }
    }

    private async void OnMenuSave(object? sender, EventArgs e)
    {
        if (_viewModel.CurrentMap is null)
        {
            return;
        }

        if (_viewModel.CurrentMap.FilePath is null)
        {
            await OnMenuSaveAsAsync();
            return;
        }

        try
        {
            _viewModel.CurrentMap.Save();
            _viewModel.StatusMessage = "Map saved.";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Save Error", ex.Message, "OK");
        }
    }

    private async void OnMenuSaveAs(object? sender, EventArgs e) =>
        await OnMenuSaveAsAsync();

    private async Task OnMenuSaveAsAsync()
    {
        if (_viewModel.CurrentMap is null)
        {
            return;
        }

        var defaultName = _viewModel.CurrentMap.FilePath is not null
            ? Path.GetFileName(_viewModel.CurrentMap.FilePath)
            : "untitled.map";

        try
        {
            using var memStream = new MemoryStream();
            _viewModel.CurrentMap.MapFile.WriteTo(memStream);
            memStream.Position = 0;

            if (!(OperatingSystem.IsAndroid()
                || OperatingSystem.IsIOSVersionAtLeast(14)
                || OperatingSystem.IsMacCatalystVersionAtLeast(14)
                || OperatingSystem.IsWindows()))
            {
                await DisplayAlertAsync("Save As Error",
                    "Save As is not available on this platform.",
                    "OK");
                return;
            }

            var fileSaver = FileSaver.Default;
            var result = await fileSaver.SaveAsync(defaultName, memStream, CancellationToken.None);

            if (result.IsSuccessful)
            {
                _viewModel.CurrentMap.SaveAs(result.FilePath);
                _viewModel.StatusMessage = $"Saved: {Path.GetFileName(result.FilePath)}";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Save As Error", ex.Message, "OK");
        }
    }

    private async void OnMenuExit(object? sender, EventArgs e)
    {
        if (!await PromptForUnsavedChangesAsync())
        {
            return;
        }

        Application.Current?.Quit();
    }

    private async Task<bool> PromptForUnsavedChangesAsync()
    {
        if (_viewModel.CurrentMap?.IsDirty != true)
        {
            return true;
        }

        return await DisplayAlertAsync("Unsaved Changes",
            "The current map has unsaved changes. Continue and discard them?",
            "Discard", "Cancel");
    }

    // ── Edit menu ──────────────────────────────────────────────────────────────

    private void OnMenuUndo(object? sender, EventArgs e) => _viewModel.Undo();
    private void OnMenuRedo(object? sender, EventArgs e) => _viewModel.Redo();

    private void OnMenuSelectAll(object? sender, EventArgs e)
    {
        // Select tool must be active to make sense
        _viewModel.ActiveTool = _viewModel.SelectTool;
    }

    private void OnMenuDelete(object? sender, EventArgs e) => _viewModel.DeleteSelected();

    private async void OnMenuMapSettings(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new MapSettingsPage(_viewModel.CurrentMap));

    // ── View menu ─────────────────────────────────────────────────────────────

    private void OnToggleShowGrid(object? sender, EventArgs e) =>
        _viewModel.ShowGrid = !_viewModel.ShowGrid;

    private void OnToggleShowObjects(object? sender, EventArgs e) =>
        _viewModel.ShowObjects = !_viewModel.ShowObjects;

    private void OnToggleShowWater(object? sender, EventArgs e) =>
        _viewModel.ShowWater = !_viewModel.ShowWater;

    private void OnToggleShowWaypoints(object? sender, EventArgs e) =>
        _viewModel.ShowWaypoints = !_viewModel.ShowWaypoints;

    private void OnToggleLockAngle(object? sender, EventArgs e)
    {
        _viewModel.IsLockAngle = !_viewModel.IsLockAngle;
        // TODO: wire IsLockAngle to scene camera controller
    }

    private void OnToggleTopDown(object? sender, EventArgs e)
    {
        _viewModel.IsTopDown = !_viewModel.IsTopDown;
        // TODO: wire IsTopDown to scene camera controller
    }

    private void OnTogglePassability(object? sender, EventArgs e) =>
        _viewModel.ShowPassability = !_viewModel.ShowPassability;

    // ── Windows menu ──────────────────────────────────────────────────────────

    private async void OnMenuScripts(object? sender, EventArgs e) =>
        await DisplayAlertAsync("Scripts", "Scripts Editor not yet implemented.", "OK");

    private async void OnMenuTeams(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new TeamsPage(_viewModel.CurrentMap));

    private async void OnMenuPlayerList(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new PlayerListPage(_viewModel.CurrentMap));

    private async void OnMenuMapIni(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new MapIniPage(_viewModel.CurrentMap));

    private async void OnMenuMapStr(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new MapStrEditorPage(_viewModel.CurrentMap));

    // ── Help menu ─────────────────────────────────────────────────────────────

    private async void OnMenuAbout(object? sender, EventArgs e) =>
        await DisplayAlertAsync("About OpenSAGE World Builder",
            "OpenSAGE World Builder\nVersion 0.1 (pre-release)\n\nhttps://github.com/OpenSAGE/OpenSAGE",
            "OK");

    private async void OnMenuSettings(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new SettingsPage());

    // ── Tool selection ────────────────────────────────────────────────────────

    private void OnToolSelect(object? sender, EventArgs e) =>
        _viewModel.ActiveTool = _viewModel.SelectTool;

    private void OnToolHeightMound(object? sender, EventArgs e) =>
        _viewModel.ActiveTool = _viewModel.MoundTool;

    private void OnToolHeightDig(object? sender, EventArgs e) =>
        _viewModel.ActiveTool = _viewModel.DigTool;

    private void OnToolSmooth(object? sender, EventArgs e) =>
        _viewModel.ActiveTool = _viewModel.SmoothTool;

    private void OnToolTexturePaint(object? sender, EventArgs e) =>
        _viewModel.ActiveTool = _viewModel.PaintTileTool;

    private void OnToolEyeDropper(object? sender, EventArgs e) =>
        _viewModel.ActiveTool = _viewModel.EyeDropperTool;

    private void OnToolPlaceObject(object? sender, EventArgs e) =>
        _viewModel.ActiveTool = _viewModel.PlaceObjectTool;

    private void OnToolWaypoint(object? sender, EventArgs e) =>
        _viewModel.ActiveTool = _viewModel.WaypointTool;

    // ── Keyboard shortcuts ────────────────────────────────────────────────────

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        RegisterKeyboardShortcuts();
    }

    private void RegisterKeyboardShortcuts()
    {
#if WINDOWS || MACCATALYST
        if (Shell.Current is not null)
        {
            return;
        }
#endif
    }

    /// <summary>
    /// Handles keyboard shortcuts routed from the viewport or shell.
    /// Called by platform-specific code when key events occur.
    /// </summary>
    public void HandleKeyboardShortcut(string key, bool ctrl, bool shift, bool alt)
    {
        if (ctrl)
        {
            switch (key.ToUpperInvariant())
            {
                case "Z":
                    if (shift)
                    {
                        _viewModel.Redo();
                    }
                    else
                    {
                        _viewModel.Undo();
                    }
                    return;

                case "Y":
                    _viewModel.Redo();
                    return;

                case "S":
                    if (shift)
                    {
                        _ = OnMenuSaveAsAsync();
                    }
                    else
                    {
                        OnMenuSave(null, EventArgs.Empty);
                    }
                    return;

                case "N":
                    OnMenuNew(null, EventArgs.Empty);
                    return;

                case "O":
                    OnMenuOpen(null, EventArgs.Empty);
                    return;

                case "I":
                    _viewModel.ShowPassability = !_viewModel.ShowPassability;
                    return;
            }
        }

        if (!ctrl && !shift && !alt)
        {
            switch (key.ToUpperInvariant())
            {
                case "A":
                    _viewModel.ActiveTool = _viewModel.SelectTool;
                    return;

                case "H":
                    _viewModel.ActiveTool = _viewModel.MoundTool;
                    return;

                case "D":
                    _viewModel.ActiveTool = _viewModel.DigTool;
                    return;

                case "T":
                    _viewModel.ActiveTool = _viewModel.PaintTileTool;
                    return;

                case "P":
                    _viewModel.ActiveTool = _viewModel.PlaceObjectTool;
                    return;

                case "DELETE":
                case "BACKSPACE":
                    _viewModel.DeleteSelected();
                    return;
            }
        }
    }
}
