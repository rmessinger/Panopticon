using OpenSage.Tools.ReplaySketch.Maui.ViewModels;
using OpenSage.Tools.ReplaySketch.Model;
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
    // ── Layout constants (device-independent units) ──────────────────────────
    private const float NodeW = 140f;
    private const float NodeH = 44f;
    private const float ColGap = 16f;
    private const float RowGap = 80f;
    private const float CanvasPad = 20f;

    // ── Palette metadata ─────────────────────────────────────────────────────
    private static readonly (ActionType Type, string Label, string DefaultLabel)[] PaletteItems =
    [
        (ActionType.BuildBarracks,    "Build Barracks",    "BuildBarracks"),
        (ActionType.GatherResources,  "Gather Resources",  "GatherResources"),
        (ActionType.RecruitBasicUnit, "Recruit Unit",      "RecruitBasicUnit"),
        (ActionType.AttackEnemyBase,  "Attack Enemy Base", "AttackEnemyBase"),
    ];

    // ── Default timing for palette-added actions ─────────────────────────────
    private static TimingConfig DefaultTiming(ActionType type) => type switch
    {
        ActionType.BuildBarracks => new FixedTiming(90),
        ActionType.GatherResources => new AfterActionTiming(ActionType.BuildBarracks, ActionPhase.Dispatched),
        ActionType.RecruitBasicUnit => new AfterActionTiming(ActionType.BuildBarracks, ActionPhase.Completed),
        ActionType.AttackEnemyBase => new AfterActionTiming(ActionType.RecruitBasicUnit, ActionPhase.Completed),
        _ => new FixedTiming(300),
    };

    // ── Default position for palette-added actions ────────────────────────────
    private static PositionSpec? DefaultPosition(ActionType type) => type switch
    {
        ActionType.RecruitBasicUnit => null,
        _ => new LandmarkRelativePosition(LandmarkType.OwnBase, 2f, new FixedAngle(45f)),
    };

    // ── Canvas colours ───────────────────────────────────────────────────────
    private static readonly SKColor CanvasBg = new(0x1A, 0x1A, 0x1A);
    private static readonly SKColor NodeFill = new(0x2D, 0x2D, 0x2D);
    private static readonly SKColor NodeBorderNormal = new(0x55, 0x55, 0x55);
    private static readonly SKColor NodeBorderSelect = SKColors.White;
    private static readonly SKColor TextMain = new(0xD4, 0xD4, 0xD4);
    private static readonly SKColor TextDim = new(0x88, 0x88, 0x88);
    private static readonly SKColor EdgeDispatched = new(0xD4, 0xA0, 0x20);
    private static readonly SKColor EdgeCompleted = new(0x4E, 0xC9, 0x4E);

    // ── Layout node ──────────────────────────────────────────────────────────
    private sealed class LayoutNode(ActionEntry action, int row, int col)
    {
        public ActionEntry Action { get; } = action;
        public int Row { get; } = row;
        public int Col { get; } = col;
        public float CenterX { get; set; }
        public float CenterY { get; set; }

        // MAUI logical-unit rect (for AbsoluteLayout overlay)
        public Rect MauiBounds =>
            new(CenterX - NodeW / 2, CenterY - NodeH / 2, NodeW, NodeH);

        // SkiaSharp rect (drawn in logical units after Scale(density) is applied)
        public SKRect SkBounds =>
            SKRect.Create(CenterX - NodeW / 2, CenterY - NodeH / 2, NodeW, NodeH);
    }

    // ── State ────────────────────────────────────────────────────────────────
    private MainViewModel? _vm;
    private List<LayoutNode> _layoutNodes = [];
    private LayoutNode? _selectedNode;
    private SizeF _canvasLogicalSize;
    private VerticalStackLayout? _externalDetailPanel;

    // Landmark labels / values shared with position controls
    private static readonly string[] LandmarkLabels =
        ["Own Base", "Enemy Base", "Map Center"];
    private static readonly LandmarkType[] LandmarkValues =
        [LandmarkType.OwnBase, LandmarkType.EnemyBase, LandmarkType.MapCenter];

    public ActionTreeView()
    {
        InitializeComponent();
        BuildPalette();
        SetupOverlayTap();
    }

    private void SetupOverlayTap()
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            var pos = e.GetPosition(NodeOverlay);
            if (!pos.HasValue)
                return;

            var hit = _layoutNodes.FirstOrDefault(n => n.MauiBounds.Contains(pos.Value));
            _selectedNode = hit == _selectedNode ? null : hit;
            TreeCanvas.InvalidateSurface();
            BuildDetailPanel(_selectedNode);
        };
        NodeOverlay.GestureRecognizers.Add(tap);
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
                _selectedNode = null;
                RefreshTree();
            }
        };
        RefreshTree();
    }

    public void RefreshTree()
    {
        if (_vm == null) return;
        _layoutNodes = ComputeLayout(_vm.SelectedPlayer.Actions);
        UpdateCanvasSize();
        RebuildOverlay();
        TreeCanvas.InvalidateSurface();
        BuildDetailPanel(_selectedNode);
    }

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

        foreach (var (type, label, _) in PaletteItems)
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
            btn.Clicked += (_, _) => AddFromPalette(type);

            // MAUI drag-and-drop: palette button → canvas
            var drag = new DragGestureRecognizer { CanDrag = true };
            drag.DragStarting += (_, e) =>
            {
                e.Data.Properties["action"] = "palette:" + type.ToString();
            };
            btn.GestureRecognizers.Add(drag);

            PalettePanel.Children.Add(btn);
        }

        // Fallback drop zone: palette item dropped on blank canvas → FixedTiming
        var canvasDrop = new DropGestureRecognizer { AllowDrop = true };
        canvasDrop.Drop += (_, e) => HandleDrop(e.Data.Properties, targetNode: null);
        CanvasDropZone.GestureRecognizers.Add(canvasDrop);
    }

    private void AddFromPalette(ActionType type)
    {
        if (_vm == null) return;

        TimingConfig timing;
        if (_selectedNode != null && _selectedNode.Action != null)
        {
            // Add as a child of the currently selected node
            timing = new AfterActionTiming(_selectedNode.Action.Type, ActionPhase.Completed);
        }
        else
        {
            timing = DefaultTiming(type);
        }

        var item = PaletteItems.FirstOrDefault(p => p.Type == type);
        var entry = new ActionEntry(item.DefaultLabel, type, DefaultPosition(type), timing);
        _vm.SelectedPlayer.Actions.Add(entry);
        _selectedNode = null;
        RefreshTree();
    }

    // ── Layout computation ────────────────────────────────────────────────────

    private List<LayoutNode> ComputeLayout(IReadOnlyList<ActionEntry> actions)
    {
        if (actions.Count == 0)
        {
            _canvasLogicalSize = new SizeF(200, 200);
            return [];
        }

        // Parent-lookup by ActionType
        var byType = new Dictionary<ActionType, ActionEntry>();
        foreach (var a in actions)
            byType[a.Type] = a;

        // Classify each action: root (FixedTiming/RandomTiming) or child (AfterActionTiming)
        var parentOf = new Dictionary<ActionEntry, ActionEntry>();
        var roots = new List<ActionEntry>();

        foreach (var a in actions)
        {
            if (a.Timing is AfterActionTiming aat && byType.TryGetValue(aat.TriggerAction, out var parent))
                parentOf[a] = parent;
            else
                roots.Add(a);
        }

        // BFS to assign row depths
        var rowOf = new Dictionary<ActionEntry, int>();
        var queue = new Queue<ActionEntry>(roots);
        foreach (var r in roots) rowOf[r] = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var depth = rowOf[current];
            foreach (var a in actions)
            {
                if (!rowOf.ContainsKey(a) && parentOf.TryGetValue(a, out var p) && p == current)
                {
                    rowOf[a] = depth + 1;
                    queue.Enqueue(a);
                }
            }
        }

        // Orphans (e.g., AfterActionTiming referencing a non-existent predecessor)
        var maxAssigned = rowOf.Values.DefaultIfEmpty(0).Max();
        foreach (var a in actions)
        {
            if (!rowOf.ContainsKey(a))
                rowOf[a] = ++maxAssigned;
        }

        // Group by row, preserving original list order within each row
        var byRow = actions
            .GroupBy(a => rowOf[a])
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Compute canvas logical size
        var numRows = byRow.Keys.Max() + 1;
        var maxRowWidth = byRow.Values.Max(r => r.Count * NodeW + Math.Max(0, r.Count - 1) * ColGap);
        var canvasW = maxRowWidth + 2 * CanvasPad;
        var canvasH = numRows * NodeH + Math.Max(0, numRows - 1) * RowGap + 2 * CanvasPad;
        _canvasLogicalSize = new SizeF(canvasW, canvasH);

        // Place nodes: center each row horizontally within the canvas
        var nodes = new List<LayoutNode>();
        foreach (var (row, rowActions) in byRow)
        {
            var rowWidth = rowActions.Count * NodeW + Math.Max(0, rowActions.Count - 1) * ColGap;
            var rowStartX = (canvasW - rowWidth) / 2f + NodeW / 2f;
            var centerY = CanvasPad + row * (NodeH + RowGap) + NodeH / 2f;

            for (var col = 0; col < rowActions.Count; col++)
            {
                nodes.Add(new LayoutNode(rowActions[col], row, col)
                {
                    CenterX = rowStartX + col * (NodeW + ColGap),
                    CenterY = centerY,
                });
            }
        }

        return nodes;
    }

    // ── Canvas size ───────────────────────────────────────────────────────────

    private void UpdateCanvasSize()
    {
        TreeCanvas.WidthRequest = Math.Max(_canvasLogicalSize.Width, 200);
        TreeCanvas.HeightRequest = Math.Max(_canvasLogicalSize.Height, 200);
    }

    // ── Overlay rebuild ──────────────────────────────────────────────────────

    private void RebuildOverlay()
    {
        NodeOverlay.Children.Clear();

        foreach (var node in _layoutNodes)
        {
            var capturedNode = node;
            var overlay = new BoxView
            {
                Color = Colors.Transparent,
                WidthRequest = NodeW,
                HeightRequest = NodeH,
                InputTransparent = false,
            };
            AbsoluteLayout.SetLayoutBounds(overlay, node.MauiBounds);

            // Drag this canvas node (for re-parenting)
            var drag = new DragGestureRecognizer { CanDrag = true };
            drag.DragStarting += (_, e) =>
            {
                e.Data.Properties["action"] = "node:" + capturedNode.Action.Type.ToString();
            };
            overlay.GestureRecognizers.Add(drag);

            // Drop onto this node (from palette or from another canvas node)
            var drop = new DropGestureRecognizer { AllowDrop = true };
            drop.Drop += (_, e) => HandleDrop(e.Data.Properties, capturedNode);
            overlay.GestureRecognizers.Add(drop);

            NodeOverlay.Children.Add(overlay);
        }
    }

    private void HandleDrop(IReadOnlyDictionary<string, object> properties, LayoutNode? targetNode)
    {
        if (_vm == null) return;
        if (!properties.TryGetValue("action", out var rawVal) || rawVal is not string text) return;

        if (text.StartsWith("palette:") && Enum.TryParse<ActionType>(text[8..], out var paletteType))
        {
            var timing = targetNode != null
                ? (TimingConfig)new AfterActionTiming(targetNode.Action.Type, ActionPhase.Completed)
                : new FixedTiming(300);

            var item = PaletteItems.FirstOrDefault(p => p.Type == paletteType);
            var entry = new ActionEntry(item.DefaultLabel, paletteType, DefaultPosition(paletteType), timing);
            _vm.SelectedPlayer.Actions.Add(entry);
            _selectedNode = null;
            RefreshTree();
        }
        else if (text.StartsWith("node:") && Enum.TryParse<ActionType>(text[5..], out var draggedType))
        {
            var draggedAction = _vm.SelectedPlayer.Actions.FirstOrDefault(a => a.Type == draggedType);
            if (draggedAction == null || targetNode == null || draggedAction == targetNode.Action) return;

            draggedAction.Timing = new AfterActionTiming(targetNode.Action.Type, ActionPhase.Completed);
            RefreshTree();
        }
    }

    // ── SkiaSharp painting ────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(CanvasBg);

        if (_layoutNodes.Count == 0)
        {
            using var hintPaint = new SKPaint { Color = TextDim, IsAntialias = true };
            using var hintFont = new SKFont { Size = 13 };
            canvas.DrawText("Use the palette to add actions", 24, 40, hintFont, hintPaint);
            return;
        }

        // Scale logical units → pixels
        var density = TreeCanvas.Width > 0 ? (float)(e.Info.Width / TreeCanvas.Width) : 1f;
        canvas.Save();
        canvas.Scale(density);

        DrawEdges(canvas);
        DrawNodes(canvas);

        canvas.Restore();
    }

    private void DrawEdges(SKCanvas canvas)
    {
        var byType = _layoutNodes.ToDictionary(n => n.Action.Type);

        foreach (var child in _layoutNodes)
        {
            if (child.Action.Timing is not AfterActionTiming aat) continue;
            if (!byType.TryGetValue(aat.TriggerAction, out var parent)) continue;

            var isDispatched = aat.TriggerPhase == ActionPhase.Dispatched;
            var edgeColor = isDispatched ? EdgeDispatched : EdgeCompleted;
            var phaseLabel = isDispatched ? "Dispatched" : "Completed";

            var startX = parent.CenterX;
            var startY = parent.CenterY + NodeH / 2f;
            var endX = child.CenterX;
            var endY = child.CenterY - NodeH / 2f;

            // Cubic bezier
            using var pathPaint = new SKPaint
            {
                Color = edgeColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true,
            };
            var midY = (startY + endY) / 2f;
            using var path = new SKPath();
            path.MoveTo(startX, startY);
            path.CubicTo(startX, midY, endX, midY, endX, endY);
            canvas.DrawPath(path, pathPaint);

            DrawArrowhead(canvas, endX, endY, edgeColor);

            // Phase label at curve midpoint
            using var textPaint = new SKPaint { Color = edgeColor, IsAntialias = true };
            using var textFont = new SKFont { Size = 10 };
            canvas.DrawText(phaseLabel, (startX + endX) / 2f + 4f, (startY + endY) / 2f, textFont, textPaint);
        }
    }

    private static void DrawArrowhead(SKCanvas canvas, float x, float y, SKColor color)
    {
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(x, y);
        path.LineTo(x - 5f, y - 9f);
        path.LineTo(x + 5f, y - 9f);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private void DrawNodes(SKCanvas canvas)
    {
        foreach (var node in _layoutNodes)
        {
            var b = node.SkBounds;
            var isSelected = node == _selectedNode;
            var rrect = new SKRoundRect(b, 6f, 6f);

            // Fill
            using (var bgPaint = new SKPaint { Color = NodeFill, Style = SKPaintStyle.Fill, IsAntialias = true })
                canvas.DrawRoundRect(rrect, bgPaint);

            // Border
            using (var borderPaint = new SKPaint
            {
                Color = isSelected ? NodeBorderSelect : NodeBorderNormal,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isSelected ? 2f : 1f,
                IsAntialias = true,
            })
                canvas.DrawRoundRect(rrect, borderPaint);

            // Action type (small, dimmed)
            using var typePaint = new SKPaint { Color = TextDim, IsAntialias = true };
            using var typeFont = new SKFont { Size = 9 };
            canvas.DrawText(node.Action.Type.ToString(), b.Left + 6f, b.Top + 13f, typeFont, typePaint);

            // Label (main text)
            using var labelPaint = new SKPaint { Color = TextMain, IsAntialias = true };
            using var labelFont = new SKFont { Size = 12 };
            var labelText = TruncateText(node.Action.Label, labelFont, NodeW - 10f);
            canvas.DrawText(labelText, b.Left + 6f, b.Bottom - 9f, labelFont, labelPaint);
        }
    }

    private static string TruncateText(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        while (text.Length > 1 && font.MeasureText(text + "\u2026") > maxWidth)
            text = text[..^1];
        return text + "\u2026";
    }

    // ── Detail panel ──────────────────────────────────────────────────────────

    private void BuildDetailPanel(LayoutNode? node)
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

        var action = node.Action;
        var player = _vm!.SelectedPlayer;

        _externalDetailPanel.Children.Add(SectionLabel($"Editing: {action.Label}"));

        // Type
        var typeLabels = PaletteItems.Select(p => p.Label).ToArray();
        var typeValues = PaletteItems.Select(p => p.Type).ToArray();
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
                ? new AfterActionTiming(other.Type, ActionPhase.Completed)
                : new AfterActionTiming(ActionType.BuildBarracks, ActionPhase.Completed);
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
            // Trigger action picker — all types except the action itself
            var triggerTypes = PaletteItems.Where(p => p.Type != action.Type).ToArray();
            var triggerIdx = Math.Max(0, Array.FindIndex(triggerTypes, p => p.Type == aat.TriggerAction));

            container.Children.Add(FieldRow("After action", BuildPicker(
                triggerTypes.Select(p => p.Label).ToArray(),
                triggerIdx,
                idx => action.Timing = new AfterActionTiming(
                    triggerTypes[idx].Type, aat.TriggerPhase, aat.ExtraFrames))));

            // Phase picker
            var phases = new[] { "Dispatched", "Completed" };
            var phaseIdx = aat.TriggerPhase == ActionPhase.Dispatched ? 0 : 1;
            container.Children.Add(FieldRow("Phase", BuildPicker(phases, phaseIdx,
                idx => action.Timing = new AfterActionTiming(
                    aat.TriggerAction,
                    idx == 0 ? ActionPhase.Dispatched : ActionPhase.Completed,
                    aat.ExtraFrames))));

            // Extra frames
            container.Children.Add(FieldRow("Extra frames", BuildUIntEntry(aat.ExtraFrames,
                v => action.Timing = new AfterActionTiming(aat.TriggerAction, aat.TriggerPhase, v))));
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
