using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.Mods.Generals;
using Panopticon.Tools.WorldBuilder.Avalonia.Controls;
using Panopticon.Tools.WorldBuilder.Avalonia.Services;
using Panopticon.Tools.WorldBuilder.Avalonia.ViewModels;
using Panopticon.Tools.WorldBuilder.Avalonia.Views;
using Panopticon.Tools.WorldBuilder.Core;
using Orientation = Avalonia.Layout.Orientation;

namespace Panopticon.Tools.WorldBuilder.Avalonia;

/// <summary>
/// Main editor shell: menu bar, toolbar, tool panel, 3D viewport, and status
/// bar, wired to the shared EditorViewModel and GameViewportRenderer.
/// </summary>
public sealed class MainWindow : Window
{
    private const int DefaultWidth = 1600;
    private const int DefaultHeight = 900;

    private readonly EditorViewModel _viewModel = new();
    private readonly GameViewportRenderer _viewportRenderer;
    private readonly StackPanel _leftPanelStack = new() { Spacing = 0 };
    private readonly ListBox _populationList = new() { MinHeight = 90, MaxHeight = 170 };
    private readonly TextBox _popDefinition = new() { Watermark = "Definition name" };
    private readonly TextBox _popOwner = new() { Watermark = "Owner", Width = 60 };
    private readonly TextBox _popWorldX = new() { Watermark = "World X", Width = 80 };
    private readonly TextBox _popWorldY = new() { Watermark = "World Y", Width = 80 };
    private readonly TextBlock _populationStatus = new()
    {
        Text = "Open a map to edit its scenario population.",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Foreground = new SolidColorBrush(WbTheme.Text),
    };
    private bool _populationRefreshing;
    private readonly TextBlock _statusLabel = new()
    {
        Text = "Ready",
        Foreground = Brushes.White,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public MainWindow()
    {
        Title = "Panopticon World Builder";
        Width = DefaultWidth;
        Height = DefaultHeight;
        Background = new SolidColorBrush(WbTheme.Background);

        var installation = GameInstallation.FindAll([GeneralsDefinition.Instance]).FirstOrDefault();
        _viewportRenderer = new GameViewportRenderer
        {
            Installation = installation,
        };

        var viewport = new WorldBuilderViewport
        {
            Renderer = _viewportRenderer,
        };
        viewport.KeyEventReceived += keyEvent =>
        {
            if (keyEvent.IsDown)
            {
                HandleKeyboardShortcut(
                    keyEvent.Key,
                    keyEvent.Control,
                    keyEvent.Shift,
                    alt: false);
            }
        };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewportRenderer.LoadStatus += message => _viewModel.StatusMessage = message;
        Closed += OnClosed;

        if (installation is null)
        {
            _viewModel.StatusMessage = "No supported SAGE game installation found.";
        }

        Content = BuildLayout(viewport);
    }

    private Control BuildLayout(WorldBuilderViewport viewport)
    {
        return new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                BuildToolBar(),
                BuildWorkArea(viewport),
                BuildStatusBar(),
            },
        };
    }

    private Control BuildToolBar()
    {
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2),
        };

        void AddTool(string text, string tooltip, IEditorTool tool)
        {
            var button = new Button
            {
                Content = text,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(WbTheme.Text),
                FontSize = 14,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(1, 0),
                MinWidth = 28,
                Height = 28,
                Tag = tool,
            };
            ToolTip.SetTip(button, tooltip);
            button.Click += (_, _) => _viewModel.ActiveTool = tool;
            toolbar.Children.Add(button);
        }

        void AddSeparator()
        {
            toolbar.Children.Add(new Border
            {
                Width = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Margin = new Thickness(4, 2),
            });
        }

        AddTool("↖", "Select / Arrow tool", _viewModel.SelectTool);
        AddSeparator();
        AddTool("▲", "Mound (raise terrain)", _viewModel.MoundTool);
        AddTool("▼", "Dig (lower terrain)", _viewModel.DigTool);
        AddTool("〜", "Smooth Height", _viewModel.SmoothTool);
        AddSeparator();
        AddTool("🖌", "Paint Texture", _viewModel.PaintTileTool);
        AddTool("💧", "Eye Dropper", _viewModel.EyeDropperTool);
        AddSeparator();
        AddTool("📦", "Place Object", _viewModel.PlaceObjectTool);
        AddTool("📍", "Waypoint", _viewModel.WaypointTool);

        var border = new Border
        {
            Background = new SolidColorBrush(WbTheme.Toolbar),
            BorderBrush = new SolidColorBrush(WbTheme.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 2),
            Child = toolbar,
        };
        Grid.SetRow(border, 0);

        // Menus live in the native menu bar; attach both to the layout grid.
        var grid = new Grid { Children = { border } };
        return grid;
    }

    private Control BuildWorkArea(WorldBuilderViewport viewport)
    {
        var leftPanel = new Border
        {
            Background = new SolidColorBrush(WbTheme.Panel),
            BorderBrush = new SolidColorBrush(WbTheme.Border),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer
            {
                Content = _leftPanelStack,
            },
        };

        // WI-A5-30: spatial population authoring rides the shared controller beside the map
        // tools; terrain and map edits stay in EditorMap.
        _leftPanelStack.Children.Add(BuildPopulationPanel());

        var viewportHost = new Border
        {
            Background = new SolidColorBrush(WbTheme.ViewportBack),
            Child = viewport,
        };

        var workArea = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("240,*,0"),
            Children =
            {
                leftPanel,
            },
        };
        Grid.SetColumn(viewportHost, 1);
        workArea.Children.Add(viewportHost);

        var container = new Grid();
        Grid.SetRow(workArea, 1);
        container.Children.Add(workArea);
        return container;
    }

    /// <summary>
    /// WI-A5-30: left-panel section for scenario population authoring. Every operation routes
    /// through the shared <see cref="Panopticon.Tools.WorldBuilder.Core.ScenarioPopulationDocument"/>
    /// (and its InitialPopulationEditor); nothing here touches EditorMap state.
    /// </summary>
    private Control BuildPopulationPanel()
    {
        _populationList.SelectionChanged += (_, _) =>
        {
            if (!_populationRefreshing && _viewModel.ScenarioPopulation is { } document)
            {
                document.Select(_populationList.SelectedIndex);
            }
        };

        var addButton = new Button { Content = "Add" };
        addButton.Click += (_, _) => RunPopulationOperation(document =>
        {
            return int.TryParse(_popOwner.Text, out var owner)
                && float.TryParse(_popWorldX.Text, System.Globalization.CultureInfo.InvariantCulture, out var x)
                && float.TryParse(_popWorldY.Text, System.Globalization.CultureInfo.InvariantCulture, out var y)
                && document.AddAtWorld(_popDefinition.Text.Trim(), owner, x, y);
        });

        var moveButton = new Button { Content = "Move Selected" };
        moveButton.Click += (_, _) => RunPopulationOperation(document =>
        {
            return float.TryParse(_popWorldX.Text, System.Globalization.CultureInfo.InvariantCulture, out var x)
                && float.TryParse(_popWorldY.Text, System.Globalization.CultureInfo.InvariantCulture, out var y)
                && document.MoveSelectedToWorld(x, y);
        });

        var ownerButton = new Button { Content = "Change Owner" };
        ownerButton.Click += (_, _) => RunPopulationOperation(document =>
        {
            return int.TryParse(_popOwner.Text, out var owner) && document.ChangeOwnerSelected(owner);
        });

        var deleteButton = new Button { Content = "Delete Selected" };
        deleteButton.Click += (_, _) => RunPopulationOperation(document => document.DeleteSelected());

        var saveButton = new Button { Content = "Save" };
        saveButton.Click += (_, _) =>
        {
            _viewModel.SaveScenarioPopulation();
            RefreshPopulationPanel();
        };

        return new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(8),
            Children =
            {
                new TextBlock
                {
                    Text = "Scenario population",
                    FontWeight = FontWeight.Bold,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(WbTheme.Text),
                },
                _populationList,
                _popDefinition,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { _popOwner, _popWorldX, _popWorldY },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { addButton, moveButton, ownerButton },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { deleteButton, saveButton },
                },
                _populationStatus,
            },
        };
    }

    private void RunPopulationOperation(Func<Panopticon.Tools.WorldBuilder.Core.ScenarioPopulationDocument, bool> operation)
    {
        _viewModel.EnsureScenarioPopulation();
        if (_viewModel.ScenarioPopulation is not { } document)
        {
            return;
        }

        var succeeded = operation(document);
        if (!succeeded && document.LastDiagnostics is { Count: > 0 } diagnostics)
        {
            _viewModel.StatusMessage = "Population edit rejected: " + diagnostics[0];
        }

        RefreshPopulationPanel();
    }

    private void RefreshPopulationPanel()
    {
        _populationRefreshing = true;
        try
        {
            var document = _viewModel.ScenarioPopulation;
            _populationList.IsEnabled = document is not null;
            _populationList.ItemsSource = document is null ? null : DescribePlacements(document);

            if (document is null)
            {
                _populationStatus.Text = "Open a map to edit its scenario population.";
                return;
            }

            _populationList.SelectedIndex = document.Editor.SelectedIndex;
            _populationStatus.Text = document.LastDiagnostics is { Count: > 0 } diagnostics
                ? string.Join("; ", diagnostics)
                : $"{document.Editor.Placements.Count} placement(s){(document.IsDirty ? " (unsaved)" : "")}";
        }
        finally
        {
            _populationRefreshing = false;
        }
    }

    private static IReadOnlyList<string> DescribePlacements(
        Panopticon.Tools.WorldBuilder.Core.ScenarioPopulationDocument document)
    {
        var lines = new List<string>();
        for (var i = 0; i < document.Editor.Placements.Count; i++)
        {
            var placement = document.Editor.Placements[i];
            var where = placement.Position is Panopticon.Tools.ReplaySketch.Model.NormalizedPosition normalized
                ? DescribeWorld(document, normalized)
                : "landmark-relative";
            lines.Add($"#{i} P{placement.PlayerIndex} {placement.DefinitionName} @ {where}");
        }

        return lines;
    }

    private static string DescribeWorld(
        Panopticon.Tools.WorldBuilder.Core.ScenarioPopulationDocument document,
        Panopticon.Tools.ReplaySketch.Model.NormalizedPosition position)
    {
        var (worldX, worldY) = document.NormalizedToWorld(position);
        var format = System.Globalization.CultureInfo.InvariantCulture;
        return $"({worldX.ToString("0", format)}, {worldY.ToString("0", format)})";
    }

    private Control BuildStatusBar()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(WbTheme.Accent),
            Padding = new Thickness(8, 2),
            Child = _statusLabel,
        };
        var container = new Grid();
        Grid.SetRow(border, 2);
        container.Children.Add(border);
        return container;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        NativeMenu.SetMenu(this, BuildNativeMenu());
    }

    private NativeMenu BuildNativeMenu()
    {
        var menu = new NativeMenu();

        var file = HeaderMenu("File");
        file.Menu!.Items.Add(Item("New Map…", "Ctrl+N", OnMenuNew));
        file.Menu!.Items.Add(Item("Open Map…", "Ctrl+O", OnMenuOpen));
        file.Menu!.Items.Add(new NativeMenuItemSeparator());
        file.Menu!.Items.Add(Item("Save", "Ctrl+S", OnMenuSave));
        file.Menu!.Items.Add(Item("Save As…", "Ctrl+Shift+S", OnMenuSaveAs));
        file.Menu!.Items.Add(new NativeMenuItemSeparator());
        file.Menu!.Items.Add(Item("Exit", null, OnMenuExit));
        menu.Items.Add(file);

        var edit = HeaderMenu("Edit");
        edit.Menu!.Items.Add(Item("Undo", "Ctrl+Z", (_, _) => _viewModel.Undo()));
        edit.Menu!.Items.Add(Item("Redo", "Ctrl+Y", (_, _) => _viewModel.Redo()));
        edit.Menu!.Items.Add(new NativeMenuItemSeparator());
        edit.Menu!.Items.Add(Item("Select All", null, (_, _) => _viewModel.ActiveTool = _viewModel.SelectTool));
        edit.Menu!.Items.Add(Item("Delete Selected", null, (_, _) => _viewModel.DeleteSelected()));
        edit.Menu!.Items.Add(new NativeMenuItemSeparator());
        edit.Menu!.Items.Add(Item("Map Settings…", null, (_, _) => ShowOwnedDialog(new MapSettingsWindow(_viewModel.CurrentMap))));
        menu.Items.Add(edit);

        var view = HeaderMenu("View");
        view.Menu!.Items.Add(Toggle("Show Grid", () => _viewModel.ShowGrid, v => _viewModel.ShowGrid = v));
        view.Menu!.Items.Add(Toggle("Show Objects", () => _viewModel.ShowObjects, v => _viewModel.ShowObjects = v));
        view.Menu!.Items.Add(Toggle("Show Water", () => _viewModel.ShowWater, v => _viewModel.ShowWater = v));
        view.Menu!.Items.Add(Toggle("Show Waypoints", () => _viewModel.ShowWaypoints, v => _viewModel.ShowWaypoints = v));
        view.Menu!.Items.Add(new NativeMenuItemSeparator());
        view.Menu!.Items.Add(Toggle("Lock Camera Angle", () => _viewModel.IsLockAngle, v => _viewModel.IsLockAngle = v));
        view.Menu!.Items.Add(Toggle("Top-Down View", () => _viewModel.IsTopDown, v => _viewModel.IsTopDown = v));
        view.Menu!.Items.Add(new NativeMenuItemSeparator());
        view.Menu!.Items.Add(Toggle("Show Passability (Ctrl+I)", () => _viewModel.ShowPassability, v => _viewModel.ShowPassability = v));
        menu.Items.Add(view);

        var windows = HeaderMenu("Windows");
        windows.Menu!.Items.Add(Item("Scripts…", null, async (_, _) =>
            await AppMessageBox.ShowAsync(this, "Scripts", "Scripts Editor not yet implemented.")));
        windows.Menu!.Items.Add(Item("Teams…", null, (_, _) => ShowOwnedDialog(new TeamsWindow(_viewModel.CurrentMap))));
        windows.Menu!.Items.Add(Item("Player List…", null, (_, _) => ShowOwnedDialog(new PlayerListWindow(_viewModel.CurrentMap))));
        windows.Menu!.Items.Add(new NativeMenuItemSeparator());
        windows.Menu!.Items.Add(Item("Map INI Editor…", null, (_, _) => ShowOwnedDialog(new MapIniWindow(_viewModel.CurrentMap))));
        windows.Menu!.Items.Add(Item("Map String Table…", null, (_, _) => ShowOwnedDialog(new MapStrEditorWindow(_viewModel.CurrentMap))));
        menu.Items.Add(windows);

        var help = HeaderMenu("Help");
        help.Menu!.Items.Add(Item("Settings…", null, (_, _) => ShowOwnedDialog(new SettingsWindow())));
        help.Menu!.Items.Add(new NativeMenuItemSeparator());
        help.Menu!.Items.Add(Item("About…", null, async (_, _) =>
            await AppMessageBox.ShowAsync(
                this,
                "About Panopticon World Builder",
                "Panopticon World Builder\nVersion 0.1 (pre-release)\n\nhttps://github.com/Panopticon/Panopticon")));
        menu.Items.Add(help);

        return menu;
    }

    private static NativeMenuItem HeaderMenu(string header)
    {
        return new NativeMenuItem
        {
            Header = header,
            Menu = new NativeMenu(),
        };
    }

    private static NativeMenuItem Item(
        string header, string? gesture, EventHandler handler)
    {
        var item = new NativeMenuItem
        {
            Header = header,
        };
        if (gesture is not null)
        {
            item.Gesture = KeyGesture.Parse(gesture);
        }

        item.Click += handler;
        return item;
    }

    private static NativeMenuItem Toggle(
        string header,
        Func<bool> get,
        Action<bool> set)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = get(),
        };
        item.Click += (_, _) =>
        {
            set(!get());
            item.IsChecked = get();
        };
        return item;
    }

    private async void ShowOwnedDialog(Window dialog)
    {
        await dialog.ShowDialog(this);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditorViewModel.StatusMessage):
                _statusLabel.Text = _viewModel.StatusMessage;
                break;

            case nameof(EditorViewModel.Title):
                Title = _viewModel.Title;
                break;

            case nameof(EditorViewModel.CurrentMap) when _viewModel.CurrentMap is not null:
                _viewportRenderer.LoadMap(_viewModel.CurrentMap);
                _viewModel.EnsureScenarioPopulation();
                RefreshPopulationPanel();
                break;

            case nameof(EditorViewModel.ScenarioPopulation):
                RefreshPopulationPanel();
                break;

            case nameof(EditorViewModel.ActiveTool):
                _viewportRenderer.ActiveTool = _viewModel.ActiveTool;
                _viewModel.StatusMessage = _viewModel.ActiveTool?.Name ?? "Ready";
                break;

            case nameof(EditorViewModel.ShowGrid):
                _viewportRenderer.ShowGrid = _viewModel.ShowGrid;
                break;

            case nameof(EditorViewModel.ShowObjects):
                _viewportRenderer.ShowObjects = _viewModel.ShowObjects;
                break;

            case nameof(EditorViewModel.ShowWater):
                _viewportRenderer.ShowWater = _viewModel.ShowWater;
                break;

            case nameof(EditorViewModel.ShowWaypoints):
                _viewportRenderer.ShowWaypoints = _viewModel.ShowWaypoints;
                break;

            case nameof(EditorViewModel.ShowPassability):
                _viewportRenderer.ShowPassability = _viewModel.ShowPassability;
                break;

            case nameof(EditorViewModel.IsLockAngle):
                _viewportRenderer.IsLockAngle = _viewModel.IsLockAngle;
                break;

            case nameof(EditorViewModel.IsTopDown):
                _viewportRenderer.IsTopDown = _viewModel.IsTopDown;
                break;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewportRenderer.Dispose();
    }

    // ── File menu ──────────────────────────────────────────────────────────

    private async void OnMenuNew(object? sender, EventArgs e)
    {
        if (!await PromptForUnsavedChangesAsync())
        {
            return;
        }

        var wizard = new NewMapWizardWindow();
        wizard.MapCreated += OnNewMapCreated;
        await wizard.ShowDialog(this);
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

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Map File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Generals Map") { Patterns = ["*.map"] },
            ],
        });

        if (files.Count != 1)
        {
            return;
        }

        var path = files[0].Path.LocalPath;
        try
        {
            await using var stream = File.OpenRead(path);
            var mapFile = MapFile.FromStream(stream);
            var editorMap = EditorMap.Open(mapFile, path);
            _viewModel.CurrentMap = editorMap;
            _viewModel.StatusMessage = $"Opened: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            await AppMessageBox.ShowAsync(this, "Open Error", ex.Message);
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
            await AppMessageBox.ShowAsync(this, "Save Error", ex.Message);
        }
    }

    private async void OnMenuSaveAs(object? sender, EventArgs e) => await OnMenuSaveAsAsync();

    private async Task OnMenuSaveAsAsync()
    {
        if (_viewModel.CurrentMap is null)
        {
            return;
        }

        var defaultName = _viewModel.CurrentMap.FilePath is { } existing
            ? Path.GetFileName(existing)
            : "untitled.map";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Map As",
            SuggestedFileName = defaultName,
            FileTypeChoices =
            [
                new FilePickerFileType("Generals Map") { Patterns = ["*.map"] },
            ],
        });

        if (file is null)
        {
            return;
        }

        try
        {
            var path = file.Path.LocalPath;
            _viewModel.CurrentMap.SaveAs(path);
            _viewModel.StatusMessage = $"Saved: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            await AppMessageBox.ShowAsync(this, "Save As Error", ex.Message);
        }
    }

    private async void OnMenuExit(object? sender, EventArgs e)
    {
        if (!await PromptForUnsavedChangesAsync())
        {
            return;
        }

        Close();
    }

    private async Task<bool> PromptForUnsavedChangesAsync()
    {
        if (_viewModel.CurrentMap?.IsDirty != true)
        {
            return true;
        }

        return await AppMessageBox.ShowAsync(
            this,
            "Unsaved Changes",
            "The current map has unsaved changes. Continue and discard them?",
            "Discard",
            "Cancel");
    }

    // ── Keyboard shortcuts ─────────────────────────────────────────────────

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
