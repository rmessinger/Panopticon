using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using global::Avalonia.Controls.Primitives;
using OpenSage.Tools.ReplaySketch.Avalonia.Controls;
using OpenSage.Tools.ReplaySketch.Avalonia.ViewModels;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace OpenSage.Tools.ReplaySketch.Avalonia.Views;

/// <summary>
/// Displays the action tree for the selected player slot as an interactive node graph.
/// The left palette lists available <see cref="ActionType"/> commands; tapping a palette button
/// adds the command to the current player's action list. Each node in the canvas can be
/// dragged onto another node to re-parent it via an <see cref="AfterActionTiming"/> trigger.
/// Selecting a node shows its edit controls in the detail panel. Ported from the MAUI
/// ActionTreeView; pointer events replace SKTouch.
/// </summary>
public sealed class ActionTreeView : UserControl
{
    // ── Layout constants (re-exported from ActionTreeLayoutEngine for SKiaSharp drawing) ──
    private const float NodeW = ActionTreeLayoutEngine.NodeW;
    private const float NodeH = ActionTreeLayoutEngine.NodeH;
    private const float CanvasPad = ActionTreeLayoutEngine.CanvasPad;

    private static readonly IBrush SectionBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush FieldBackground = new SolidColorBrush(Color.Parse("#3C3C3C"));
    private static readonly IBrush PaletteIdle = new SolidColorBrush(Color.Parse("#3C3C3C"));
    private static readonly IBrush PaletteActive = new SolidColorBrush(Color.Parse("#0E639C"));

    // ── State ────────────────────────────────────────────────────────────────
    private MainViewModel? _vm;
    private ActionTreeController? _controller;
    private IReadOnlyList<NodeLayout> _layoutNodes = [];
    private NodeLayout? _selectedNode;
    private global::Avalonia.Size _canvasLogicalSize;
    private StackPanel? _externalDetailPanel;

    // Pending selection to apply once a new controller is created for a different player
    private string? _pendingSelectId;

    // ── Drag state ───────────────────────────────────────────────────────────
    private const float DragThresholdPx = 8f;
    private string? _dragId;
    private float _dragStartX;
    private float _dragStartY;
    private float _dragCurrentX;
    private float _dragCurrentY;
    private bool _isDragging;

    // Tracks which palette button is highlighted (UI-only, not in controller)
    private Button? _flyoutActiveButton;

    // Landmark labels / values shared with position controls
    private static readonly string[] LandmarkLabels =
        ["Own Base", "Enemy Base", "Map Center"];
    private static readonly LandmarkType[] LandmarkValues =
        [LandmarkType.OwnBase, LandmarkType.EnemyBase, LandmarkType.MapCenter];

    private readonly SkiaCanvas _treeCanvas = new();
    private readonly StackPanel _palettePanel = new() { Spacing = 4, Margin = new Thickness(6) };
    private readonly Panel _flyoutOverlay = new() { IsVisible = false };
    private readonly Border _flyoutBackdrop = new() { Background = new SolidColorBrush(Color.Parse("#80000000")) };
    private readonly Border _flyoutCard = new();
    private readonly StackPanel _flyoutContent = new() { Spacing = 8 };

    public ActionTreeView()
    {
        _treeCanvas.PaintSurface += OnPaintSurface;
        _treeCanvas.PointerPressed += OnCanvasPointerPressed;
        _treeCanvas.PointerMoved += OnCanvasPointerMoved;
        _treeCanvas.PointerReleased += OnCanvasPointerReleased;

        BuildPalette();

        _flyoutBackdrop.PointerPressed += (_, _) => HideFlyout();
        _flyoutCard = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2D2D2D")),
            BorderBrush = new SolidColorBrush(Color.Parse("#666")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth = 280,
            Child = _flyoutContent,
        };
        _flyoutOverlay.Children.Add(_flyoutBackdrop);
        _flyoutOverlay.Children.Add(_flyoutCard);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("104,*") };
        var paletteBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#444")),
            Background = new SolidColorBrush(Color.Parse("#222222")),
            BorderThickness = new Thickness(1),
        };
        paletteBorder.SetValue(Grid.ColumnProperty, 0);
        paletteBorder.Child = new ScrollViewer { Content = _palettePanel };

        var treeScroll = new ScrollViewer
        {
            Content = _treeCanvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        treeScroll.SetValue(Grid.ColumnProperty, 1);

        grid.Children.Add(paletteBorder);
        grid.Children.Add(treeScroll);
        grid.Children.Add(_flyoutOverlay);
        Content = grid;
    }

    // ── Pointer hit-testing ─────────────────────────────────────────────────

    private SKPoint ToCanvasPoint(PointerEventArgs e)
    {
        var position = e.GetPosition(_treeCanvas);
        return new SKPoint((float)position.X, (float)position.Y);
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        var location = ToCanvasPoint(e);

        var hit = _layoutNodes.FirstOrDefault(n => ActionTreeLayoutEngine.HitTest(n, location.X, location.Y));
        if (hit.Action != null)
        {
            _dragId = hit.Action.Id;
            _dragStartX = location.X;
            _dragStartY = location.Y;
            _dragCurrentX = location.X;
            _dragCurrentY = location.Y;
            _isDragging = false;
        }
        else
        {
            _dragId = null;
            _isDragging = false;
            _controller?.SelectNode(null);
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragId == null) return;
        e.Handled = true;

        var location = ToCanvasPoint(e);
        _dragCurrentX = location.X;
        _dragCurrentY = location.Y;
        var dx = _dragCurrentX - _dragStartX;
        var dy = _dragCurrentY - _dragStartY;
        if (!_isDragging && MathF.Sqrt(dx * dx + dy * dy) >= DragThresholdPx)
            _isDragging = true;
        if (_isDragging)
            _treeCanvas.InvalidateCanvas();
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = true;
        var location = ToCanvasPoint(e);

        if (_dragId != null && _isDragging)
        {
            _controller?.Reparent(
                _dragId,
                GetDropTargetId(location.X, location.Y, _dragId));
        }
        else if (_dragId != null)
        {
            // Short tap — just select
            _controller?.SelectNode(_dragId);
        }
        _dragId = null;
        _isDragging = false;
        _treeCanvas.InvalidateCanvas();
    }

    private string? GetDropTargetId(float logX, float logY, string excludeId)
    {
        foreach (var node in _layoutNodes)
        {
            if (node.Action.Id != excludeId && ActionTreeLayoutEngine.HitTest(node, logX, logY))
                return node.Action.Id;
        }
        return null;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Provides the external <see cref="StackPanel"/> (owned by the right panel)
    /// that this view writes selected-node edit controls into.
    /// </summary>
    public void SetExternalDetailPanel(StackPanel panel)
    {
        _externalDetailPanel = panel;
        BuildDetailPanel(_selectedNode);
    }

    /// <summary>
    /// Selects an action from an external view (e.g. the map view).
    /// Handles the case where the action belongs to a different player slot —
    /// the slot is switched first, and the selection is applied once the new controller is ready.
    /// </summary>
    public void SelectActionFromMap(int playerSlotIndex, string actionId)
    {
        if (_vm == null) return;
        if (playerSlotIndex != _vm.SelectedSlotIndex)
        {
            _pendingSelectId = actionId;
            _vm.SelectedSlotIndex = playerSlotIndex;  // triggers AttachController via PropertyChanged
        }
        else
        {
            _controller?.SelectNode(actionId);
        }
    }

    /// <summary>
    /// Refreshes the detail panel and repaints the tree canvas for the currently selected node.
    /// Called by external views (e.g. the map view) after they mutate an action's position.
    /// </summary>
    public void RefreshSelectedNode()
    {
        _selectedNode = FindNode(_controller?.SelectedActionId);
        BuildDetailPanel(_selectedNode);
        _treeCanvas.InvalidateCanvas();
    }

    public void BindViewModel(MainViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SelectedSlotIndex)
                                or nameof(MainViewModel.SelectedPlayer))
            {
                AttachController(vm.SelectedPlayer);
                _selectedNode = null;
                RefreshTree();
            }
        };
        AttachController(vm.SelectedPlayer);
        RefreshTree();
    }

    private void AttachController(PlayerSlotConfig player)
    {
        // Reset palette button highlight before swapping controllers
        if (_flyoutActiveButton != null)
        {
            _flyoutActiveButton.Background = PaletteIdle;
            _flyoutActiveButton = null;
        }
        _flyoutOverlay.IsVisible = false;

        if (_vm != null) _vm.SelectedActionId = null;

        _controller = new ActionTreeController(player);
        _controller.ActionsChanged += (_, _) => RefreshTree();
        _controller.SelectionChanged += (_, _) => SyncSelection();
        _controller.FlyoutStateChanged += (_, _) => SyncFlyout();

        // Apply any selection that was requested before the player switch completed
        if (_pendingSelectId != null)
        {
            var id = _pendingSelectId;
            _pendingSelectId = null;
            _controller.SelectNode(id);
        }
    }

    private void SyncSelection()
    {
        _selectedNode = FindNode(_controller?.SelectedActionId);
        if (_vm != null) _vm.SelectedActionId = _controller?.SelectedActionId;
        BuildDetailPanel(_selectedNode);
        _treeCanvas.InvalidateCanvas();
    }

    private void SyncFlyout()
    {
        if (_controller == null) return;

        if (_controller.FlyoutIsOpen)
        {
            var item = ActionTreeController.PaletteItems.FirstOrDefault(p => p.Type == _controller.FlyoutType);
            _flyoutCard.Margin = new Thickness(110, 40, 0, 0);
            BuildFlyoutContent(item.Label, _vm!.SelectedPlayer.FactionIndex);
            _flyoutOverlay.IsVisible = true;
        }
        else
        {
            _flyoutOverlay.IsVisible = false;
        }
    }

    public void RefreshTree()
    {
        if (_vm == null) return;
        _layoutNodes = ComputeLayout(_vm.SelectedPlayer.Actions);
        _selectedNode = FindNode(_controller?.SelectedActionId);
        UpdateCanvasSize();
        _treeCanvas.InvalidateCanvas();
        BuildDetailPanel(_selectedNode);
    }

    private NodeLayout? FindNode(string? id) =>
        id == null ? null
        : _layoutNodes.FirstOrDefault(n => n.Action.Id == id) is { Action: not null } found
            ? found
            : null;

    // ── Palette ──────────────────────────────────────────────────────────────

    private void BuildPalette()
    {
        _palettePanel.Children.Clear();

        _palettePanel.Children.Add(new TextBlock
        {
            Text = "Add Action",
            FontWeight = FontWeight.Bold,
            Foreground = SectionBrush,
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var (type, label, _) in ActionTreeController.PaletteItems)
        {
            var btn = new Button
            {
                Content = label,
                Background = PaletteIdle,
                Foreground = FieldBrush,
                Padding = new Thickness(4, 6),
            };
            btn.Click += (_, _) => ToggleAddFlyout(type, btn);

            _palettePanel.Children.Add(btn);
        }
    }

    // ── Add-Action flyout ─────────────────────────────────────────────────────

    private void ToggleAddFlyout(ActionType type, Button sourceButton)
    {
        if (_controller == null) return;

        // Determine new open state before calling controller (it will toggle)
        var willOpen = !(_controller.FlyoutIsOpen && _controller.FlyoutType == type);

        if (willOpen)
        {
            _flyoutActiveButton?.Background = PaletteIdle;
            _flyoutActiveButton = sourceButton;
            sourceButton.Background = PaletteActive;
        }
        else
        {
            _flyoutActiveButton?.Background = PaletteIdle;
            _flyoutActiveButton = null;
        }

        _controller.OpenFlyout(type);  // fires FlyoutStateChanged → SyncFlyout
    }

    private void BuildFlyoutContent(string typeLabel, int factionIndex)
    {
        _flyoutContent.Children.Clear();

        _flyoutContent.Children.Add(new TextBlock
        {
            Text = $"Add: {typeLabel}",
            FontWeight = FontWeight.Bold,
            Foreground = SectionBrush,
        });

        var labelEntry = new TextBox
        {
            Text = _controller!.FlyoutDraft!.Label,
            Watermark = "Label",
            Foreground = FieldBrush,
            Background = FieldBackground,
        };
        labelEntry.TextChanged += (_, _) => _controller!.SetFlyoutLabel(labelEntry.Text ?? string.Empty);
        _flyoutContent.Children.Add(FieldRow("Label", labelEntry));

        if (_controller.FlyoutType == ActionType.Construct)
            _flyoutContent.Children.Add(FieldRow("Building",
                BuildBuildingPicker(_controller.FlyoutDraft, factionIndex)));

        if (_controller.FlyoutType == ActionType.RecruitBasicUnit)
            _flyoutContent.Children.Add(FieldRow("Unit",
                BuildUnitPicker(_controller.FlyoutDraft, factionIndex)));

        var buttonsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Background = new SolidColorBrush(Color.Parse("#555")),
            Foreground = FieldBrush,
        };
        cancelBtn.Click += (_, _) => HideFlyout();

        var addBtn = new Button
        {
            Content = "Add",
            Background = PaletteActive,
            Foreground = Brushes.White,
        };
        addBtn.Click += (_, _) =>
        {
            _controller?.CommitFlyout();  // fires FlyoutStateChanged + ActionsChanged
            if (_flyoutActiveButton != null)
            {
                _flyoutActiveButton.Background = PaletteIdle;
                _flyoutActiveButton = null;
            }
        };

        buttonsRow.Children.Add(cancelBtn);
        buttonsRow.Children.Add(addBtn);
        _flyoutContent.Children.Add(buttonsRow);
    }

    private void HideFlyout()
    {
        _controller?.CancelFlyout();  // fires FlyoutStateChanged → SyncFlyout
        if (_flyoutActiveButton != null)
        {
            _flyoutActiveButton.Background = PaletteIdle;
            _flyoutActiveButton = null;
        }
    }

    // ── Layout computation ────────────────────────────────────────────────────

    private IReadOnlyList<NodeLayout> ComputeLayout(IReadOnlyList<ActionEntry> actions)
    {
        var layouts = ActionTreeLayoutEngine.ComputeLayout(actions);

        if (layouts.Count == 0)
        {
            _canvasLogicalSize = new global::Avalonia.Size(200, 200);
            return [];
        }

        _canvasLogicalSize = new global::Avalonia.Size(
            layouts.Max(n => n.CenterX) + NodeW / 2f + CanvasPad,
            layouts.Max(n => n.CenterY) + ActionTreeLayoutEngine.NodeH / 2f + CanvasPad);

        return layouts;
    }

    private void UpdateCanvasSize()
    {
        _treeCanvas.DesiredCanvasSize = new Size(
            Math.Max(_canvasLogicalSize.Width, 200),
            Math.Max(_canvasLogicalSize.Height, 200));
    }

    // ── SkiaSharp painting ────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKCanvas canvas)
    {
        DragState? drag = _isDragging && _dragId != null
            ? new DragState(
                _dragId,
                _dragCurrentX,
                _dragCurrentY,
                GetDropTargetId(_dragCurrentX, _dragCurrentY, _dragId))
            : null;

        ActionTreeRenderer.Render(canvas, _layoutNodes, _controller?.SelectedActionId, 1f, drag);
    }

    // ── Detail panel ──────────────────────────────────────────────────────────

    private void BuildDetailPanel(NodeLayout? node)
    {
        if (_externalDetailPanel == null) return;
        _externalDetailPanel.Children.Clear();

        if (node == null)
        {
            _externalDetailPanel.Children.Add(new TextBlock
            {
                Text = "Select a node to edit it",
                Foreground = new SolidColorBrush(Color.Parse("#888888")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16),
            });
            return;
        }

        var action = node.Value.Action;
        var player = _vm!.SelectedPlayer;

        _externalDetailPanel.Children.Add(SectionLabel($"Editing: {action.Label}"));

        // Type
        var typeLabels = ActionTreeController.PaletteItems.Select(p => p.Label).ToArray();
        var typeValues = ActionTreeController.PaletteItems.Select(p => p.Type).ToArray();
        _externalDetailPanel.Children.Add(FieldRow("Type", BuildPicker(
            typeLabels,
            Math.Max(0, Array.IndexOf(typeValues, action.Type)),
            idx =>
            {
                action.Type = typeValues[idx];
                if (action.Type == ActionType.RecruitBasicUnit)
                    action.Position = null;
                else if (action.Position == null)
                    action.Position = new LandmarkRelativePosition(LandmarkType.OwnBase, 2f, new FixedAngle(45f));
                RefreshTree();
            })));

        // Label
        _externalDetailPanel.Children.Add(FieldRow("Label",
            BuildEntry(action.Label, v => { action.Label = v; _treeCanvas.InvalidateCanvas(); })));

        // Building definition name (Construct only)
        if (action.Type == ActionType.Construct)
        {
            var factionIndex = _vm!.SelectedPlayer.FactionIndex;
            _externalDetailPanel.Children.Add(FieldRow("Building",
                BuildBuildingPicker(action, factionIndex, () => _treeCanvas.InvalidateCanvas())));
        }

        // Unit definition name (RecruitBasicUnit only)
        if (action.Type == ActionType.RecruitBasicUnit)
        {
            var factionIndex = _vm!.SelectedPlayer.FactionIndex;
            _externalDetailPanel.Children.Add(FieldRow("Unit",
                BuildUnitPicker(action, factionIndex, () => _treeCanvas.InvalidateCanvas())));
        }

        // Position
        if (action.Position != null)
        {
            _externalDetailPanel.Children.Add(SectionLabel("Position"));
            _externalDetailPanel.Children.Add(BuildPositionControls(action));
        }

        // Timing
        _externalDetailPanel.Children.Add(SectionLabel("Timing"));
        _externalDetailPanel.Children.Add(BuildTimingControls(action));

        // Remove button
        var removeBtn = new Button
        {
            Content = "Remove Action",
            Background = new SolidColorBrush(Color.Parse("#6B1A1A")),
            Foreground = Brushes.White,
            Margin = new Thickness(0, 8, 0, 0),
        };
        removeBtn.Click += (_, _) =>
        {
            player.Actions.Remove(action);
            _selectedNode = null;
            RefreshTree();
        };
        _externalDetailPanel.Children.Add(removeBtn);
    }

    // ── Position controls ─────────────────────────────────────────────────────

    private Control BuildPositionControls(ActionEntry action)
    {
        var container = new StackPanel { Spacing = 4 };
        var isNormalized = action.Position is NormalizedPosition;

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var normBtn = new RadioButton { Content = "Map Normalized", IsChecked = isNormalized };
        var lmBtn = new RadioButton { Content = "Landmark Relative", IsChecked = !isNormalized };
        normBtn.Click += (_, _) =>
        {
            if (normBtn.IsChecked != true) return;
            action.Position = new NormalizedPosition(0.5f, 0.5f);
            RefreshTree();
        };
        lmBtn.Click += (_, _) =>
        {
            if (lmBtn.IsChecked != true) return;
            action.Position = new LandmarkRelativePosition(LandmarkType.OwnBase, 2f, new FixedAngle(45f));
            RefreshTree();
        };
        modeRow.Children.Add(normBtn);
        modeRow.Children.Add(lmBtn);
        container.Children.Add(modeRow);

        if (action.Position is NormalizedPosition np)
        {
            container.Children.Add(FieldRow("X (0\u20131)", BuildFloatEntry(np.NormX, v =>
            {
                action.Position = new NormalizedPosition(Math.Clamp(v, 0f, 1f), np.NormY);
            })));
            container.Children.Add(FieldRow("Y (0\u20131)", BuildFloatEntry(np.NormY, v =>
            {
                action.Position = new NormalizedPosition(np.NormX, Math.Clamp(v, 0f, 1f));
            })));
        }
        else if (action.Position is LandmarkRelativePosition lrp)
        {
            container.Children.Add(FieldRow("Landmark", BuildPicker(
                LandmarkLabels,
                Array.IndexOf(LandmarkValues, lrp.Landmark),
                idx => action.Position = new LandmarkRelativePosition(
                    LandmarkValues[idx], lrp.DistanceInBaseWidths, lrp.Angle))));
            container.Children.Add(FieldRow("Distance (base widths)", BuildFloatEntry(lrp.DistanceInBaseWidths, v =>
                action.Position = new LandmarkRelativePosition(lrp.Landmark, Math.Max(0.1f, v), lrp.Angle))));
            container.Children.Add(SectionLabel("Angle"));
            container.Children.Add(BuildAngleControls(action, lrp));
        }

        return container;
    }

    private Control BuildAngleControls(ActionEntry action, LandmarkRelativePosition lrp)
    {
        var container = new StackPanel { Spacing = 4 };
        var isFixed = lrp.Angle is FixedAngle;

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var fixedBtn = new RadioButton { Content = "Fixed", IsChecked = isFixed };
        var randomBtn = new RadioButton { Content = "Random range", IsChecked = !isFixed };
        fixedBtn.Click += (_, _) =>
        {
            if (fixedBtn.IsChecked != true) return;
            action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new FixedAngle(0f));
            RefreshTree();
        };
        randomBtn.Click += (_, _) =>
        {
            if (randomBtn.IsChecked != true) return;
            action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new RandomAngle(0f, 360f));
            RefreshTree();
        };
        modeRow.Children.Add(fixedBtn);
        modeRow.Children.Add(randomBtn);
        container.Children.Add(modeRow);

        if (lrp.Angle is FixedAngle fa)
        {
            container.Children.Add(FieldRow("Degrees", BuildFloatEntry(fa.Degrees, v =>
                action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new FixedAngle(v)))));
        }
        else if (lrp.Angle is RandomAngle ra)
        {
            container.Children.Add(FieldRow("Min\u00B0", BuildFloatEntry(ra.MinDegrees, v =>
            {
                var min = v; var max = Math.Max(v, ra.MaxDegrees);
                action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new RandomAngle(min, max));
            })));
            container.Children.Add(FieldRow("Max\u00B0", BuildFloatEntry(ra.MaxDegrees, v =>
            {
                var max = v; var min = Math.Min(ra.MinDegrees, v);
                action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new RandomAngle(min, max));
            })));
        }

        return container;
    }

    // ── Timing controls ───────────────────────────────────────────────────────

    private Control BuildTimingControls(ActionEntry action)
    {
        var container = new StackPanel { Spacing = 4 };

        var isFixed = action.Timing is FixedTiming;
        var isRandom = action.Timing is RandomTiming;
        var isAfter = action.Timing is AfterActionTiming;

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var fixedBtn = new RadioButton { Content = "Fixed", IsChecked = isFixed };
        var randomBtn = new RadioButton { Content = "Random", IsChecked = isRandom };
        var afterBtn = new RadioButton { Content = "After Action", IsChecked = isAfter };

        fixedBtn.Click += (_, _) =>
        {
            if (fixedBtn.IsChecked != true) return;
            action.Timing = new FixedTiming(300);
            RefreshTree();
        };
        randomBtn.Click += (_, _) =>
        {
            if (randomBtn.IsChecked != true) return;
            action.Timing = new RandomTiming(150, 450);
            RefreshTree();
        };
        afterBtn.Click += (_, _) =>
        {
            if (afterBtn.IsChecked != true) return;
            var other = _vm!.SelectedPlayer.Actions.FirstOrDefault(a => a != action);
            action.Timing = other != null
                ? new AfterActionTiming(other.Id, ActionPhase.Completed)
                : new FixedTiming(300);  // no other actions to trigger from
            RefreshTree();
        };

        modeRow.Children.Add(fixedBtn);
        modeRow.Children.Add(randomBtn);
        modeRow.Children.Add(afterBtn);
        container.Children.Add(modeRow);

        if (action.Timing is FixedTiming ft)
        {
            container.Children.Add(FieldRow("Gap (frames)", BuildUIntEntry(ft.Frames,
                v => action.Timing = new FixedTiming(Math.Max(1u, v)))));
        }
        else if (action.Timing is RandomTiming rt)
        {
            container.Children.Add(FieldRow("Min (frames)", BuildUIntEntry(rt.MinFrames, v =>
            {
                var min = Math.Max(1u, v);
                action.Timing = new RandomTiming(min, Math.Max(min, rt.MaxFrames));
            })));
            container.Children.Add(FieldRow("Max (frames)", BuildUIntEntry(rt.MaxFrames, v =>
            {
                var max = Math.Max(1u, v);
                action.Timing = new RandomTiming(Math.Min(rt.MinFrames, max), max);
            })));
        }
        else if (action.Timing is AfterActionTiming aat)
        {
            // Trigger action picker — show actual sibling actions (not abstract types)
            var otherActions = _vm!.SelectedPlayer.Actions.Where(a => a != action).ToArray();
            if (otherActions.Length == 0)
            {
                container.Children.Add(new TextBlock
                {
                    Text = "No other actions to trigger from.",
                    Foreground = new SolidColorBrush(Color.Parse("#888")),
                });
            }
            else
            {
                var triggerIdx = Math.Max(0, Array.FindIndex(otherActions, a => a.Id == aat.TriggerActionId));

                container.Children.Add(FieldRow("After action", BuildPicker(
                    otherActions.Select(a => $"{a.Id}: {a.Label}").ToArray(),
                    triggerIdx,
                    idx =>
                    {
                        action.Timing = new AfterActionTiming(
                            otherActions[idx].Id, aat.TriggerPhase, aat.ExtraFrames);
                        RefreshTree();
                    })));

                var phases = new[] { "Dispatched", "Completed" };
                var phaseIdx = aat.TriggerPhase == ActionPhase.Dispatched ? 0 : 1;
                container.Children.Add(FieldRow("Phase", BuildPicker(phases, phaseIdx,
                    idx =>
                    {
                        action.Timing = new AfterActionTiming(
                            aat.TriggerActionId,
                            idx == 0 ? ActionPhase.Dispatched : ActionPhase.Completed,
                            aat.ExtraFrames);
                        RefreshTree();
                    })));

                container.Children.Add(FieldRow("Extra frames", BuildUIntEntry(aat.ExtraFrames,
                    v =>
                    {
                        action.Timing = new AfterActionTiming(aat.TriggerActionId, aat.TriggerPhase, v);
                        RefreshTree();
                    })));
            }
        }

        return container;
    }

    // ── Shared UI helpers ────────────────────────────────────────────────────

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Foreground = SectionBrush,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static Control FieldRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = FieldBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        return grid;
    }

    private static ComboBox BuildBuildingPicker(ActionEntry action, int factionIndex, Action? onChanged = null)
    {
        var options = BuildingCatalog.ForFaction(factionIndex);
        var displayNames = options.Select(b => b.DisplayName).ToArray();
        var currentIdx = options
            .Select((b, i) => (b, i))
            .FirstOrDefault(t => t.b.DefinitionName == action.BuildingDefinitionName).i;

        return BuildPicker(displayNames, currentIdx, idx =>
        {
            action.BuildingDefinitionName = options[idx].DefinitionName;
            onChanged?.Invoke();
        });
    }

    private static ComboBox BuildUnitPicker(ActionEntry action, int factionIndex, Action? onChanged = null)
    {
        var options = UnitCatalog.ForFaction(factionIndex);
        var displayNames = options.Select(u => u.DisplayName).ToArray();
        var currentIdx = options
            .Select((u, i) => (u, i))
            .FirstOrDefault(t => t.u.DefinitionName == action.UnitDefinitionName).i;

        return BuildPicker(displayNames, currentIdx, idx =>
        {
            action.UnitDefinitionName = options[idx].DefinitionName;
            onChanged?.Invoke();
        });
    }

    private static ComboBox BuildPicker(string[] items, int selectedIndex, Action<int> onChange)
    {
        var picker = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = Math.Max(0, selectedIndex),
            Foreground = FieldBrush,
            Background = FieldBackground,
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0) onChange(picker.SelectedIndex);
        };
        return picker;
    }

    private static TextBox BuildEntry(string value, Action<string> onChange)
    {
        var entry = new TextBox
        {
            Text = value,
            Foreground = FieldBrush,
            Background = FieldBackground,
        };
        void Commit() => onChange(entry.Text ?? string.Empty);
        entry.LostFocus += (_, _) => Commit();
        entry.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Commit();
        }, RoutingStrategies.Tunnel);
        return entry;
    }

    private static TextBox BuildFloatEntry(float value, Action<float> onChange)
    {
        var entry = new TextBox
        {
            Text = value.ToString("G6"),
            Foreground = FieldBrush,
            Background = FieldBackground,
        };
        void Commit()
        {
            if (float.TryParse(entry.Text, out var v)) onChange(v);
        }
        entry.LostFocus += (_, _) => Commit();
        entry.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Commit();
        }, RoutingStrategies.Tunnel);
        return entry;
    }

    private static TextBox BuildUIntEntry(uint value, Action<uint> onChange)
    {
        var entry = new TextBox
        {
            Text = value.ToString(),
            Foreground = FieldBrush,
            Background = FieldBackground,
        };
        void Commit()
        {
            if (uint.TryParse(entry.Text, out var v)) onChange(v);
        }
        entry.LostFocus += (_, _) => Commit();
        entry.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Commit();
        }, RoutingStrategies.Tunnel);
        return entry;
    }
}
