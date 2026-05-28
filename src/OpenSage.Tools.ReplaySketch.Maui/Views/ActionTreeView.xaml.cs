using OpenSage.Tools.ReplaySketch.Maui.ViewModels;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace OpenSage.Tools.ReplaySketch.Maui.Views;

/// <summary>
/// Displays the action tree for the selected player slot as an interactive node graph.
/// The left palette lists available <see cref="ActionType"/> commands; tapping a palette button
/// adds the command to the current player's action list.  Each node in the canvas can be
/// dragged onto another node to re-parent it via an <see cref="AfterActionTiming"/> trigger.
/// Selecting a node shows its edit controls in the detail panel below.
/// </summary>
public partial class ActionTreeView : ContentView
{
    // ── Layout constants (re-exported from ActionTreeLayoutEngine for SKiaSharp drawing) ──
    private const float NodeW = ActionTreeLayoutEngine.NodeW;
    private const float NodeH = ActionTreeLayoutEngine.NodeH;
    private const float ColGap = ActionTreeLayoutEngine.ColGap;
    private const float RowGap = ActionTreeLayoutEngine.RowGap;
    private const float CanvasPad = ActionTreeLayoutEngine.CanvasPad;

    // ── State ────────────────────────────────────────────────────────────────
    private MainViewModel? _vm;
    private ActionTreeController? _controller;
    private IReadOnlyList<NodeLayout> _layoutNodes = [];
    private NodeLayout? _selectedNode;
    private SizeF _canvasLogicalSize;
    private VerticalStackLayout? _externalDetailPanel;

    // Tracks which palette button is highlighted (UI-only, not in controller)
    private Button? _flyoutActiveButton;

    // Landmark labels / values shared with position controls
    private static readonly string[] LandmarkLabels =
        ["Own Base", "Enemy Base", "Map Center"];
    private static readonly LandmarkType[] LandmarkValues =
        [LandmarkType.OwnBase, LandmarkType.EnemyBase, LandmarkType.MapCenter];

    public ActionTreeView()
    {
        InitializeComponent();
        BuildPalette();
        FlyoutBackdrop.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(HideFlyout)
        });
    }

    // ── Touch hit-testing ─────────────────────────────────────────────────────

    private void OnCanvasTouch(object? sender, SKTouchEventArgs e)
    {
        if (e.ActionType != SKTouchAction.Pressed) return;
        e.Handled = true;

        // Touch coordinates are in physical pixels; convert to logical units
        // using the same density factor applied in OnPaintSurface.
        var density = TreeCanvas.CanvasSize.Width > 0 && TreeCanvas.Width > 0
            ? (float)(TreeCanvas.CanvasSize.Width / TreeCanvas.Width)
            : 1f;
        var logX = e.Location.X / density;
        var logY = e.Location.Y / density;

        var hit = _layoutNodes.FirstOrDefault(n => ActionTreeLayoutEngine.HitTest(n, logX, logY));
        _controller?.SelectNode(hit.Action?.Id);
        // SelectionChanged → SyncSelection handles the rest
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Provides the external <see cref="VerticalStackLayout"/> (owned by the right panel)
    /// that this view writes selected-node edit controls into.
    /// </summary>
    public void SetExternalDetailPanel(VerticalStackLayout panel)
    {
        _externalDetailPanel = panel;
        BuildDetailPanel(_selectedNode);
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
            _flyoutActiveButton.BackgroundColor = Color.FromArgb("#3C3C3C");
            _flyoutActiveButton = null;
        }
        FlyoutOverlay.IsVisible = false;

        _controller = new ActionTreeController(player);
        _controller.ActionsChanged += (_, _) => RefreshTree();
        _controller.SelectionChanged += (_, _) => SyncSelection();
        _controller.FlyoutStateChanged += (_, _) => SyncFlyout();
    }

    private void SyncSelection()
    {
        _selectedNode = FindNode(_controller?.SelectedActionId);
        BuildDetailPanel(_selectedNode);
        TreeCanvas.InvalidateSurface();
    }

    private void SyncFlyout()
    {
        if (_controller == null) return;

        if (_controller.FlyoutIsOpen)
        {
            var item = ActionTreeController.PaletteItems.FirstOrDefault(p => p.Type == _controller.FlyoutType);
            FlyoutCard.Margin = new Thickness(110, 40, 0, 0);
            BuildFlyoutContent(item.Label, _vm!.SelectedPlayer.FactionIndex);
            FlyoutOverlay.IsVisible = true;
        }
        else
        {
            FlyoutOverlay.IsVisible = false;
        }
    }

    public void RefreshTree()
    {
        if (_vm == null) return;
        _layoutNodes = ComputeLayout(_vm.SelectedPlayer.Actions);
        _selectedNode = FindNode(_controller?.SelectedActionId);
        UpdateCanvasSize();
        TreeCanvas.InvalidateSurface();
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
        PalettePanel.Children.Clear();

        var header = new Label
        {
            Text = "Add Action",
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#569CD6"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
        };
        PalettePanel.Children.Add(header);

        foreach (var (type, label, _) in ActionTreeController.PaletteItems)
        {
            var btn = new Button
            {
                Text = label,
                BackgroundColor = Color.FromArgb("#3C3C3C"),
                TextColor = Color.FromArgb("#D4D4D4"),
                FontSize = 11,
                Padding = new Thickness(4, 6),
                CornerRadius = 4,
            };
            btn.Clicked += (_, _) => ToggleAddFlyout(type, btn);

            PalettePanel.Children.Add(btn);
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
            _flyoutActiveButton?.BackgroundColor = Color.FromArgb("#3C3C3C");
            _flyoutActiveButton = sourceButton;
            sourceButton.BackgroundColor = Color.FromArgb("#0E639C");
        }
        else
        {
            _flyoutActiveButton?.BackgroundColor = Color.FromArgb("#3C3C3C");
            _flyoutActiveButton = null;
        }

        _controller.OpenFlyout(type);  // fires FlyoutStateChanged → SyncFlyout
    }

    private void BuildFlyoutContent(string typeLabel, int factionIndex)
    {
        FlyoutContent.Children.Clear();

        // Title
        FlyoutContent.Children.Add(new Label
        {
            Text = $"Add: {typeLabel}",
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#569CD6"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // Label field
        var labelEntry = new Entry
        {
            Text = _controller!.FlyoutDraft!.Label,
            Placeholder = "Label",
            TextColor = Color.FromArgb("#D4D4D4"),
            BackgroundColor = Color.FromArgb("#3C3C3C"),
            FontSize = 13,
        };
        labelEntry.TextChanged += (_, e) => _controller!.SetFlyoutLabel(e.NewTextValue);
        FlyoutContent.Children.Add(FieldRow("Label", labelEntry));

        // Building picker for Construct
        if (_controller.FlyoutType == ActionType.Construct)
            FlyoutContent.Children.Add(FieldRow("Building",
                BuildBuildingPicker(_controller.FlyoutDraft, factionIndex)));

        // Unit picker for RecruitBasicUnit
        if (_controller.FlyoutType == ActionType.RecruitBasicUnit)
            FlyoutContent.Children.Add(FieldRow("Unit",
                BuildUnitPicker(_controller.FlyoutDraft, factionIndex)));

        // Action row
        var buttonsRow = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var cancelBtn = new Button
        {
            Text = "Cancel",
            BackgroundColor = Color.FromArgb("#555"),
            TextColor = Color.FromArgb("#D4D4D4"),
            CornerRadius = 4,
            FontSize = 12,
            Padding = new Thickness(12, 6),
        };
        cancelBtn.Clicked += (_, _) => HideFlyout();

        var addBtn = new Button
        {
            Text = "Add",
            BackgroundColor = Color.FromArgb("#0E639C"),
            TextColor = Colors.White,
            CornerRadius = 4,
            FontSize = 12,
            Padding = new Thickness(12, 6),
        };
        addBtn.Clicked += (_, _) =>
        {
            _controller?.CommitFlyout();  // fires FlyoutStateChanged + ActionsChanged
            if (_flyoutActiveButton != null)
            {
                _flyoutActiveButton.BackgroundColor = Color.FromArgb("#3C3C3C");
                _flyoutActiveButton = null;
            }
        };

        buttonsRow.Children.Add(cancelBtn);
        buttonsRow.Children.Add(addBtn);
        FlyoutContent.Children.Add(buttonsRow);
    }

    private void HideFlyout()
    {
        _controller?.CancelFlyout();  // fires FlyoutStateChanged → SyncFlyout
        if (_flyoutActiveButton != null)
        {
            _flyoutActiveButton.BackgroundColor = Color.FromArgb("#3C3C3C");
            _flyoutActiveButton = null;
        }
    }

    // ── Layout computation ────────────────────────────────────────────────────

    private IReadOnlyList<NodeLayout> ComputeLayout(IReadOnlyList<ActionEntry> actions)
    {
        var layouts = ActionTreeLayoutEngine.ComputeLayout(actions);

        if (layouts.Count == 0)
        {
            _canvasLogicalSize = new SizeF(200, 200);
            return [];
        }

        _canvasLogicalSize = new SizeF(
            layouts.Max(n => n.CenterX) + NodeW / 2f + CanvasPad,
            layouts.Max(n => n.CenterY) + NodeH / 2f + CanvasPad);

        return layouts;
    }

    // ── Canvas size ───────────────────────────────────────────────────────────

    private void UpdateCanvasSize()
    {
        TreeCanvas.WidthRequest = Math.Max(_canvasLogicalSize.Width, 200);
        TreeCanvas.HeightRequest = Math.Max(_canvasLogicalSize.Height, 200);
    }

    // ── SkiaSharp painting ────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var density = TreeCanvas.Width > 0 ? (float)(e.Info.Width / TreeCanvas.Width) : 1f;
        ActionTreeRenderer.Render(e.Surface.Canvas, _layoutNodes, _controller?.SelectedActionId, density);
    }

    // ── Detail panel ──────────────────────────────────────────────────────────

    private void BuildDetailPanel(NodeLayout? node)
    {
        if (_externalDetailPanel == null) return;
        _externalDetailPanel.Children.Clear();

        if (node == null)
        {
            _externalDetailPanel.Children.Add(new Label
            {
                Text = "Select a node to edit it",
                TextColor = Color.FromArgb("#888888"),
                HorizontalOptions = LayoutOptions.Center,
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
            BuildEntry(action.Label, v => { action.Label = v; TreeCanvas.InvalidateSurface(); })));

        // Building definition name (Construct only)
        if (action.Type == ActionType.Construct)
        {
            var factionIndex = _vm!.SelectedPlayer.FactionIndex;
            _externalDetailPanel.Children.Add(FieldRow("Building",
                BuildBuildingPicker(action, factionIndex, () => TreeCanvas.InvalidateSurface())));
        }

        // Unit definition name (RecruitBasicUnit only)
        if (action.Type == ActionType.RecruitBasicUnit)
        {
            var factionIndex = _vm!.SelectedPlayer.FactionIndex;
            _externalDetailPanel.Children.Add(FieldRow("Unit",
                BuildUnitPicker(action, factionIndex, () => TreeCanvas.InvalidateSurface())));
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
            Text = "Remove Action",
            BackgroundColor = Color.FromArgb("#6B1A1A"),
            TextColor = Colors.White,
            FontSize = 12,
            CornerRadius = 4,
            Margin = new Thickness(0, 8, 0, 0),
        };
        removeBtn.Clicked += (_, _) =>
        {
            player.Actions.Remove(action);
            _selectedNode = null;
            RefreshTree();
        };
        _externalDetailPanel.Children.Add(removeBtn);
    }

    // ── Position controls ─────────────────────────────────────────────────────

    private View BuildPositionControls(ActionEntry action)
    {
        var container = new VerticalStackLayout { Spacing = 4 };
        var isNormalized = action.Position is NormalizedPosition;

        var modeRow = new HorizontalStackLayout { Spacing = 12 };
        var normBtn = new RadioButton { Content = "Map Normalized", IsChecked = isNormalized };
        var lmBtn = new RadioButton { Content = "Landmark Relative", IsChecked = !isNormalized };
        normBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Position = new NormalizedPosition(0.5f, 0.5f);
            RefreshTree();
        };
        lmBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Position = new LandmarkRelativePosition(LandmarkType.OwnBase, 2f, new FixedAngle(45f));
            RefreshTree();
        };
        modeRow.Children.Add(normBtn);
        modeRow.Children.Add(lmBtn);
        container.Children.Add(modeRow);

        if (action.Position is NormalizedPosition np)
        {
            container.Children.Add(FieldRow("X (0–1)", BuildFloatEntry(np.NormX, v =>
            {
                action.Position = new NormalizedPosition(Math.Clamp(v, 0f, 1f), np.NormY);
            })));
            container.Children.Add(FieldRow("Y (0–1)", BuildFloatEntry(np.NormY, v =>
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

    private View BuildAngleControls(ActionEntry action, LandmarkRelativePosition lrp)
    {
        var container = new VerticalStackLayout { Spacing = 4 };
        var isFixed = lrp.Angle is FixedAngle;

        var modeRow = new HorizontalStackLayout { Spacing = 12 };
        var fixedBtn = new RadioButton { Content = "Fixed", IsChecked = isFixed };
        var randomBtn = new RadioButton { Content = "Random range", IsChecked = !isFixed };
        fixedBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new FixedAngle(0f));
            RefreshTree();
        };
        randomBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
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
            container.Children.Add(FieldRow("Min°", BuildFloatEntry(ra.MinDegrees, v =>
            {
                var min = v; var max = Math.Max(v, ra.MaxDegrees);
                action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new RandomAngle(min, max));
            })));
            container.Children.Add(FieldRow("Max°", BuildFloatEntry(ra.MaxDegrees, v =>
            {
                var max = v; var min = Math.Min(ra.MinDegrees, v);
                action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new RandomAngle(min, max));
            })));
        }

        return container;
    }

    // ── Timing controls ───────────────────────────────────────────────────────

    private View BuildTimingControls(ActionEntry action)
    {
        var container = new VerticalStackLayout { Spacing = 4 };

        var isFixed = action.Timing is FixedTiming;
        var isRandom = action.Timing is RandomTiming;
        var isAfter = action.Timing is AfterActionTiming;

        var modeRow = new HorizontalStackLayout { Spacing = 8 };
        var fixedBtn = new RadioButton { Content = "Fixed", IsChecked = isFixed };
        var randomBtn = new RadioButton { Content = "Random", IsChecked = isRandom };
        var afterBtn = new RadioButton { Content = "After Action", IsChecked = isAfter };

        fixedBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Timing = new FixedTiming(300);
            RefreshTree();
        };
        randomBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Timing = new RandomTiming(150, 450);
            RefreshTree();
        };
        afterBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
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
                container.Children.Add(new Label
                {
                    Text = "No other actions to trigger from.",
                    TextColor = Color.FromArgb("#888"),
                    FontSize = 13,
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

                // Phase picker
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

                // Extra frames
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

    // ── Shared UI helpers (mirror ActionSequenceView pattern) ─────────────────

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#569CD6"),
        FontSize = 12,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static View FieldRow(string label, View control)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(130)),
                new ColumnDefinition(GridLength.Star),
            },
        };
        var lbl = new Label
        {
            Text = label,
            TextColor = Color.FromArgb("#D4D4D4"),
            VerticalOptions = LayoutOptions.Center,
            FontSize = 12,
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        return grid;
    }

    private static Picker BuildBuildingPicker(ActionEntry action, int factionIndex, Action? onChanged = null)
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

    private static Picker BuildUnitPicker(ActionEntry action, int factionIndex, Action? onChanged = null)
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

    private static Picker BuildPicker(string[] items, int selectedIndex, Action<int> onChange)
    {
        var picker = new Picker
        {
            ItemsSource = items,
            SelectedIndex = selectedIndex,
            TextColor = Color.FromArgb("#D4D4D4"),
            BackgroundColor = Color.FromArgb("#3C3C3C"),
            FontSize = 12,
        };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0) onChange(picker.SelectedIndex);
        };
        return picker;
    }

    private static Entry BuildEntry(string value, Action<string> onChange)
    {
        var entry = new Entry
        {
            Text = value,
            TextColor = Color.FromArgb("#D4D4D4"),
            BackgroundColor = Color.FromArgb("#3C3C3C"),
            FontSize = 12,
        };
        entry.Completed += (_, _) => onChange(entry.Text ?? string.Empty);
        entry.Unfocused += (_, _) => onChange(entry.Text ?? string.Empty);
        return entry;
    }

    private static Entry BuildFloatEntry(float value, Action<float> onChange)
    {
        var entry = new Entry
        {
            Text = value.ToString("G6"),
            Keyboard = Keyboard.Numeric,
            TextColor = Color.FromArgb("#D4D4D4"),
            BackgroundColor = Color.FromArgb("#3C3C3C"),
            FontSize = 12,
        };
        void Commit()
        {
            if (float.TryParse(entry.Text, out var v)) onChange(v);
        }
        entry.Completed += (_, _) => Commit();
        entry.Unfocused += (_, _) => Commit();
        return entry;
    }

    private static Entry BuildUIntEntry(uint value, Action<uint> onChange)
    {
        var entry = new Entry
        {
            Text = value.ToString(),
            Keyboard = Keyboard.Numeric,
            TextColor = Color.FromArgb("#D4D4D4"),
            BackgroundColor = Color.FromArgb("#3C3C3C"),
            FontSize = 12,
        };
        void Commit()
        {
            if (uint.TryParse(entry.Text, out var v)) onChange(v);
        }
        entry.Completed += (_, _) => Commit();
        entry.Unfocused += (_, _) => Commit();
        return entry;
    }
}
