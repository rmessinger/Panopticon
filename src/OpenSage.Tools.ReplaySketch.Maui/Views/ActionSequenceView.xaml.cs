using OpenSage.Tools.ReplaySketch.Maui.ViewModels;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Maui.Views;

/// <summary>
/// Displays the action list for the currently selected player slot.
/// Each action is rendered as a collapsible card built in code-behind so that
/// the arbitrarily-typed sub-controls (position mode, angle type, etc.) can be
/// created and wired without fighting the XAML type system.
/// </summary>
public partial class ActionSequenceView : ContentView
{
    private static readonly string[] ActionTypeLabels =
        ["Construct", "Recruit Basic Unit", "Attack Enemy Base"];
    private static readonly ActionType[] ActionTypeValues =
        [ActionType.Construct, ActionType.RecruitBasicUnit, ActionType.AttackEnemyBase];

    private static readonly string[] LandmarkLabels =
        ["Own Base", "Enemy Base", "Map Center"];
    private static readonly LandmarkType[] LandmarkValues =
        [LandmarkType.OwnBase, LandmarkType.EnemyBase, LandmarkType.MapCenter];

    private MainViewModel? _vm;

    public ActionSequenceView()
    {
        InitializeComponent();
    }

    public void BindViewModel(MainViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SelectedSlotIndex)
                                or nameof(MainViewModel.SelectedPlayer))
                Refresh();
        };
        Refresh();
    }

    public void Refresh()
    {
        if (_vm == null) return;
        ActionList.Children.Clear();

        var player = _vm.SelectedPlayer;
        var headerLabel = new Label
        {
            Text = $"Actions — {player.Name}",
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#569CD6"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        ActionList.Children.Add(headerLabel);

        for (var i = 0; i < player.Actions.Count; i++)
        {
            ActionList.Children.Add(BuildActionCard(player.Actions[i], i, player));
        }
    }

    private View BuildActionCard(ActionEntry action, int actionIdx, PlayerSlotConfig player)
    {
        // Outer card frame
        var card = new Border
        {
            Stroke = Color.FromArgb("#555"),
            BackgroundColor = Color.FromArgb("#2D2D2D"),
            Padding = new Thickness(8),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4) },
        };

        var content = new VerticalStackLayout { Spacing = 4 };

        // ── Action type ────────────────────────────────────────────────────
        content.Children.Add(FieldRow("Action", BuildPicker(
            ActionTypeLabels,
            Array.IndexOf(ActionTypeValues, action.Type),
            idx =>
            {
                var newType = ActionTypeValues[idx];
                if (newType == action.Type) return;
                action.Type = newType;
                if (newType == ActionType.RecruitBasicUnit)
                    action.Position = null;
                else if (action.Position == null)
                    action.Position = new LandmarkRelativePosition(LandmarkType.OwnBase, 2f, new FixedAngle(45f));
                if (newType == ActionType.Construct)
                    action.BuildingDefinitionName ??= BuildingCatalog.ForFaction(player.FactionIndex)
                        .FirstOrDefault(b => b.DisplayName == "Barracks")?.DefinitionName
                        ?? BuildingCatalog.ForFaction(player.FactionIndex).FirstOrDefault()?.DefinitionName;
                else
                    action.BuildingDefinitionName = null;
                if (newType == ActionType.RecruitBasicUnit)
                    action.UnitDefinitionName ??= UnitCatalog.ForFaction(player.FactionIndex).FirstOrDefault()?.DefinitionName;
                else
                    action.UnitDefinitionName = null;
                Refresh();
            })));

        // ── Building definition name (Construct only) ──────────────────────
        if (action.Type == ActionType.Construct)
        {
            content.Children.Add(FieldRow("Building",
                BuildBuildingPicker(action, player.FactionIndex, Refresh)));
        }

        // ── Unit definition name (RecruitBasicUnit only) ───────────────────
        if (action.Type == ActionType.RecruitBasicUnit)
        {
            content.Children.Add(FieldRow("Unit",
                BuildUnitPicker(action, player.FactionIndex, Refresh)));
        }

        // ── Label ──────────────────────────────────────────────────────────
        content.Children.Add(FieldRow("Label", BuildEntry(action.Label, v => action.Label = v)));

        // ── Position ──────────────────────────────────────────────────────
        if (action.Position != null)
        {
            content.Children.Add(SectionLabel("Position"));
            content.Children.Add(BuildPositionControls(action));
        }

        // ── Timing ────────────────────────────────────────────────────────
        content.Children.Add(SectionLabel("Timing"));
        content.Children.Add(BuildTimingControls(action));

        card.Content = content;
        return card;
    }

    // ── Position controls ────────────────────────────────────────────────

    private View BuildPositionControls(ActionEntry action)
    {
        var container = new VerticalStackLayout { Spacing = 4 };

        var isNormalized = action.Position is NormalizedPosition;

        // Mode toggle
        var modeRow = new HorizontalStackLayout { Spacing = 12 };
        var normBtn = new RadioButton { Content = "Map Normalized", IsChecked = isNormalized, Value = "Normalized" };
        var lmBtn = new RadioButton { Content = "Landmark Relative", IsChecked = !isNormalized, Value = "Landmark" };
        normBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Position = new NormalizedPosition(0.5f, 0.5f);
            Refresh();
        };
        lmBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Position = new LandmarkRelativePosition(LandmarkType.OwnBase, 2f, new FixedAngle(45f));
            Refresh();
        };
        modeRow.Children.Add(normBtn);
        modeRow.Children.Add(lmBtn);
        container.Children.Add(modeRow);

        if (action.Position is NormalizedPosition np)
        {
            container.Children.Add(FieldRow("X (0–1)", BuildFloatEntry(np.NormX, v =>
            {
                var clamped = Math.Clamp(v, 0f, 1f);
                action.Position = new NormalizedPosition(clamped, np.NormY);
            })));
            container.Children.Add(FieldRow("Y (0–1)", BuildFloatEntry(np.NormY, v =>
            {
                var clamped = Math.Clamp(v, 0f, 1f);
                action.Position = new NormalizedPosition(np.NormX, clamped);
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
                action.Position = new LandmarkRelativePosition(
                    lrp.Landmark, Math.Max(0.1f, v), lrp.Angle))));

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
            Refresh();
        };
        randomBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Position = new LandmarkRelativePosition(lrp.Landmark, lrp.DistanceInBaseWidths, new RandomAngle(0f, 360f));
            Refresh();
        };
        modeRow.Children.Add(fixedBtn);
        modeRow.Children.Add(randomBtn);
        container.Children.Add(modeRow);

        if (lrp.Angle is FixedAngle fa)
        {
            container.Children.Add(FieldRow("Degrees", BuildFloatEntry(fa.Degrees, v =>
                action.Position = new LandmarkRelativePosition(
                    lrp.Landmark, lrp.DistanceInBaseWidths, new FixedAngle(v)))));
        }
        else if (lrp.Angle is RandomAngle ra)
        {
            container.Children.Add(FieldRow("Min°", BuildFloatEntry(ra.MinDegrees, v =>
            {
                var max = Math.Max(v, ra.MaxDegrees);
                action.Position = new LandmarkRelativePosition(
                    lrp.Landmark, lrp.DistanceInBaseWidths, new RandomAngle(v, max));
            })));
            container.Children.Add(FieldRow("Max°", BuildFloatEntry(ra.MaxDegrees, v =>
            {
                var min = Math.Min(v, ra.MinDegrees);
                action.Position = new LandmarkRelativePosition(
                    lrp.Landmark, lrp.DistanceInBaseWidths, new RandomAngle(min, v));
            })));
        }

        return container;
    }

    // ── Timing controls ──────────────────────────────────────────────────

    private View BuildTimingControls(ActionEntry action)
    {
        var container = new VerticalStackLayout { Spacing = 4 };
        var isFixed = action.Timing is FixedTiming;

        var modeRow = new HorizontalStackLayout { Spacing = 12 };
        var fixedBtn = new RadioButton { Content = "Fixed", IsChecked = isFixed };
        var randomBtn = new RadioButton { Content = "Random range", IsChecked = !isFixed };
        fixedBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Timing = new FixedTiming(300);
            Refresh();
        };
        randomBtn.CheckedChanged += (_, e) =>
        {
            if (!e.Value) return;
            action.Timing = new RandomTiming(150, 450);
            Refresh();
        };
        modeRow.Children.Add(fixedBtn);
        modeRow.Children.Add(randomBtn);
        container.Children.Add(modeRow);

        if (action.Timing is FixedTiming ft)
        {
            container.Children.Add(FieldRow("Gap (frames)", BuildUIntEntry(ft.Frames,
                v => action.Timing = new FixedTiming(Math.Max(1u, v)))));
        }
        else if (action.Timing is RandomTiming rt)
        {
            container.Children.Add(FieldRow("Min gap (frames)", BuildUIntEntry(rt.MinFrames, v =>
            {
                var min = Math.Max(1u, v);
                var max = Math.Max(min, rt.MaxFrames);
                action.Timing = new RandomTiming(min, max);
            })));
            container.Children.Add(FieldRow("Max gap (frames)", BuildUIntEntry(rt.MaxFrames, v =>
            {
                var max = Math.Max(1u, v);
                var min = Math.Min(max, rt.MinFrames);
                action.Timing = new RandomTiming(min, max);
            })));
        }

        return container;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

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
            ColumnDefinitions = { new ColumnDefinition(new GridLength(130)), new ColumnDefinition(GridLength.Star) },
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
