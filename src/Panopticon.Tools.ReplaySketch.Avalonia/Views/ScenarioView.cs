using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using global::Avalonia.Interactivity;
using Panopticon.Tools.ReplaySketch.Avalonia.ViewModels;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.ViewModels;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Views;

/// <summary>
/// Left-panel scenario editor: map picker, base radius, player slots, and the selected
/// slot's faction/color/start/team fields. Ported from the MAUI ScenarioView.
/// </summary>
public sealed class ScenarioView : UserControl
{
    private static readonly IBrush SectionBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush FieldBackground = new SolidColorBrush(Color.Parse("#3C3C3C"));

    private MainViewModel? _vm;
    private bool _suppressCallbacks;

    private readonly ComboBox _mapPicker = new();
    private readonly TextBox _baseRadiusEntry = new();
    private readonly Button _previewButton = new() { Content = "Preview (60 frames)" };
    private readonly TextBlock _previewSummaryText = new();
    private readonly ListBox _slotsList = new();
    private readonly TextBox _playerNameEntry = new();
    private readonly ComboBox _factionPicker = new();
    private readonly TextBox _colorEntry = new();
    private readonly TextBox _startPosEntry = new();
    private readonly TextBox _teamEntry = new();
    private readonly Panel _slotDetailPanel = new();

    private ScenarioParametersViewModel? _parameters;
    private readonly ComboBox _domainCombo = new();
    private readonly TextBox _seedEntry = new();
    private readonly TextBox _frameCapEntry = new();
    private readonly ComboBox _recordingModeCombo = new();
    private readonly TextBox _sampleIntervalEntry = new();
    private readonly TextBox _snapshotIntervalEntry = new();
    private readonly ListBox _parametersList = new();
    private readonly TextBox _paramKeyEntry = new();
    private readonly TextBox _paramValueEntry = new();
    private readonly Button _addParameterButton = new() { Content = "Add" };
    private readonly Button _removeParameterButton = new() { Content = "Remove selected" };
    private readonly Button _applyParametersButton = new() { Content = "Apply" };
    private readonly TextBlock _parametersErrorText = new();

    public ScenarioView()
    {
        _mapPicker.SelectionChanged += OnMapPickerChanged;
        _baseRadiusEntry.LostFocus += (_, _) => CommitBaseRadius();
        _baseRadiusEntry.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) CommitBaseRadius();
        }, RoutingStrategies.Tunnel);

        _slotsList.SelectionChanged += OnSlotSelectionChanged;
        _playerNameEntry.LostFocus += (_, _) => CommitPlayerName();
        _playerNameEntry.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) CommitPlayerName();
        }, RoutingStrategies.Tunnel);
        _factionPicker.SelectionChanged += OnFactionPickerChanged;
        _colorEntry.LostFocus += (_, _) => CommitColor();
        _startPosEntry.LostFocus += (_, _) => CommitStartPos();
        _teamEntry.LostFocus += (_, _) => CommitTeam();

        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(8) };

        panel.Children.Add(SectionLabel("Map"));
        StyleCombo(_mapPicker);
        panel.Children.Add(_mapPicker);

        panel.Children.Add(SectionLabel("Map Settings"));
        panel.Children.Add(FieldRow("Base radius (wu)", NumericEntry(_baseRadiusEntry)));

        panel.Children.Add(SectionLabel("Player Slots"));
        _slotsList.ItemsSource = Array.Empty<string>();
        panel.Children.Add(_slotsList);

        _slotDetailPanel.Children.Add(PlainLabel("Name"));
        StyleEntry(_playerNameEntry);
        _slotDetailPanel.Children.Add(_playerNameEntry);
        _slotDetailPanel.Children.Add(PlainLabel("Faction"));
        StyleCombo(_factionPicker);
        _slotDetailPanel.Children.Add(_factionPicker);
        _slotDetailPanel.Children.Add(FieldRow("Color", NumericEntry(_colorEntry)));
        _slotDetailPanel.Children.Add(FieldRow("Start position", NumericEntry(_startPosEntry)));
        _slotDetailPanel.Children.Add(FieldRow("Team", NumericEntry(_teamEntry)));
        panel.Children.Add(_slotDetailPanel);

        panel.Children.Add(SectionLabel("Preview"));
        panel.Children.Add(_previewButton);
        _previewSummaryText.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_previewSummaryText);

        panel.Children.Add(SectionLabel("Parameters"));
        StyleCombo(_domainCombo);
        panel.Children.Add(FieldRow("Domain", _domainCombo));
        panel.Children.Add(FieldRow("Seed", _seedEntry));
        panel.Children.Add(FieldRow("Frame cap", _frameCapEntry));
        StyleCombo(_recordingModeCombo);
        panel.Children.Add(FieldRow("Recording mode", _recordingModeCombo));
        panel.Children.Add(FieldRow("Sample interval", _sampleIntervalEntry));
        panel.Children.Add(FieldRow("Snapshot interval", _snapshotIntervalEntry));
        StyleEntry(_paramKeyEntry);
        _paramKeyEntry.Watermark = "key";
        StyleEntry(_paramValueEntry);
        _paramValueEntry.Watermark = "value";
        panel.Children.Add(FieldRow("Add parameter", HStack(_paramKeyEntry, _paramValueEntry, _addParameterButton)));
        _parametersList.MinHeight = 60;
        _parametersList.MaxHeight = 120;
        panel.Children.Add(_parametersList);
        panel.Children.Add(HStack(_removeParameterButton, _applyParametersButton));
        _parametersErrorText.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_parametersErrorText);

        Content = new ScrollViewer { Content = panel };
    }

    public void BindViewModel(MainViewModel vm)
    {
        _vm = vm;
        _previewButton.Command = vm.PreviewCommand;
        _previewSummaryText.Text = vm.PreviewSummary ?? string.Empty;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.MapsGeneration))
            {
                RefreshAvailableMaps();
                Refresh();
            }

            if (e.PropertyName == nameof(MainViewModel.PreviewSummary))
            {
                _previewSummaryText.Text = vm.PreviewSummary ?? string.Empty;
            }
        };
        RefreshAvailableMaps();
        Refresh();
        BindParameters(vm);
    }

    private void BindParameters(MainViewModel vm)
    {
        _parameters = new ScenarioParametersViewModel(vm.Scenario);
        _domainCombo.ItemsSource = _parameters.DomainOptions;
        _recordingModeCombo.ItemsSource = _parameters.RecordingModeOptions;
        _parametersList.ItemsSource = _parameters.Parameters;
        _parametersList.ItemTemplate = new FuncDataTemplate<ScenarioParameterEntry>((entry, _) =>
            new TextBlock { Text = $"{entry?.Key} = {entry?.Value}", TextWrapping = TextWrapping.Wrap });

        _domainCombo.SelectionChanged += (_, _) =>
        {
            if (_parameters is { } parameters && _domainCombo.SelectedItem is string domain)
            {
                parameters.DomainText = domain;
            }
        };
        _recordingModeCombo.SelectionChanged += (_, _) =>
        {
            if (_parameters is { } parameters && _recordingModeCombo.SelectedItem is string mode)
            {
                parameters.RecordingModeText = mode;
            }
        };
        _seedEntry.TextChanged += (_, _) => SyncParameterText(p => p.SeedText = _seedEntry.Text);
        _frameCapEntry.TextChanged += (_, _) => SyncParameterText(p => p.FrameCapText = _frameCapEntry.Text);
        _sampleIntervalEntry.TextChanged += (_, _) => SyncParameterText(p => p.SampleIntervalText = _sampleIntervalEntry.Text);
        _snapshotIntervalEntry.TextChanged += (_, _) => SyncParameterText(p => p.SnapshotIntervalText = _snapshotIntervalEntry.Text);

        _addParameterButton.Click += (_, _) =>
        {
            if (_parameters is { } parameters)
            {
                parameters.AddParameter(_paramKeyEntry.Text.Trim(), _paramValueEntry.Text);
                _paramKeyEntry.Text = string.Empty;
                _paramValueEntry.Text = string.Empty;
            }
        };
        _removeParameterButton.Click += (_, _) => _parameters?.RemoveSelected();
        _applyParametersButton.Click += (_, _) => RefreshParameterErrors();
        _parameters.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScenarioParametersViewModel.LastErrors))
            {
                RefreshParameterErrors();
            }
        };

        _seedEntry.Text = _parameters.SeedText;
        _frameCapEntry.Text = _parameters.FrameCapText;
        _sampleIntervalEntry.Text = _parameters.SampleIntervalText;
        _snapshotIntervalEntry.Text = _parameters.SnapshotIntervalText;
        SyncComboSelection(_domainCombo, _parameters.DomainText);
        SyncComboSelection(_recordingModeCombo, _parameters.RecordingModeText);
        RefreshParameterErrors();
    }

    private void SyncParameterText(Action<ScenarioParametersViewModel> setter)
    {
        if (_parameters is { } parameters)
        {
            setter(parameters);
        }
    }

    private static void SyncComboSelection(ComboBox combo, string value)
    {
        if (combo.ItemsSource is IReadOnlyList<string> options)
        {
            var index = 0;
            for (var i = 0; i < options.Count; i++)
            {
                if (options[i] == value)
                {
                    index = i;
                }
            }

            combo.SelectedIndex = index;
        }
    }

    private void RefreshParameterErrors()
    {
        _parametersErrorText.Text = _parameters is null
            ? string.Empty
            : string.Join(Environment.NewLine, _parameters.LastErrors);
    }

    private void RefreshAvailableMaps()
    {
        if (_vm == null) return;

        _suppressCallbacks = true;
        _mapPicker.ItemsSource = _vm.AvailableMaps;
        _mapPicker.SelectedIndex = Math.Max(0, _vm.GetDiscoveredMapIndex(_vm.Scenario.MapPath));
        _suppressCallbacks = false;
    }

    public void Refresh()
    {
        if (_vm == null) return;
        _suppressCallbacks = true;

        var scenario = _vm.Scenario;

        var mapIdx = _vm.GetDiscoveredMapIndex(scenario.MapPath);
        _mapPicker.SelectedIndex = mapIdx >= 0 ? mapIdx : 0;

        _baseRadiusEntry.Text = scenario.BaseRadiusWorldUnits.ToString("F0");

        var slotItems = scenario.Players.Select((p, i) => $"Slot {i}: {p.Name}").ToArray();
        _slotsList.ItemsSource = slotItems;

        RefreshSelectedSlot();
        _suppressCallbacks = false;
    }

    private void RefreshSelectedSlot()
    {
        if (_vm == null) return;
        var player = _vm.SelectedPlayer;
        var slotIdx = _vm.SelectedSlotIndex;

        _slotsList.SelectedItem =
            _slotsList.ItemsSource is string[] items && slotIdx < items.Length
                ? items[slotIdx]
                : null;

        _slotDetailPanel.IsVisible = true;

        _playerNameEntry.Text = player.Name;

        var factionLabels = new[] { "USA (2)", "China (3)", "GLA (4)", "USA Gen (5)", "Toxin (6)", "Demo (7)",
            "Stealth (8)", "Nuke (9)", "Laser (10)", "Air Force (11)", "Super Weapon (12)", "Tank (13)" };
        var factionIndices = new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

        _factionPicker.ItemsSource = factionLabels;
        var factionIdx = Array.IndexOf(factionIndices, player.FactionIndex);
        _factionPicker.SelectedIndex = factionIdx >= 0 ? factionIdx : 0;

        _colorEntry.Text = player.Color.ToString();
        _startPosEntry.Text = player.StartPosition.ToString();
        _teamEntry.Text = player.Team.ToString();
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void OnMapPickerChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCallbacks || _vm == null) return;
        var idx = _mapPicker.SelectedIndex;
        var mapPath = _vm.GetDiscoveredMapPath(idx);
        if (mapPath != null && !string.Equals(_vm.Scenario.MapPath, mapPath, StringComparison.OrdinalIgnoreCase))
        {
            _vm.Scenario.MapPath = mapPath;
            _vm.LoadMap();
        }
    }

    private void CommitBaseRadius()
    {
        if (_suppressCallbacks || _vm == null) return;
        if (float.TryParse(_baseRadiusEntry.Text, out var v) && v > 0)
            _vm.Scenario.BaseRadiusWorldUnits = v;
    }

    private void OnSlotSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCallbacks || _vm == null) return;
        if (_slotsList.ItemsSource is string[] items)
        {
            var idx = _slotsList.SelectedItem is string selected ? Array.IndexOf(items, selected) : -1;
            if (idx >= 0)
            {
                _vm.SelectedSlotIndex = idx;
                _suppressCallbacks = true;
                RefreshSelectedSlot();
                _suppressCallbacks = false;
            }
        }
    }

    private void CommitPlayerName()
    {
        if (_suppressCallbacks || _vm == null) return;
        _vm.SelectedPlayer.Name = _playerNameEntry.Text ?? string.Empty;
    }

    private void OnFactionPickerChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCallbacks || _vm == null) return;
        var factionIndices = new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };
        var idx = _factionPicker.SelectedIndex;
        if (idx >= 0 && idx < factionIndices.Length)
            _vm.SelectedPlayer.FactionIndex = factionIndices[idx];
    }

    private void CommitColor()
    {
        if (_suppressCallbacks || _vm == null) return;
        if (sbyte.TryParse(_colorEntry.Text, out var v))
            _vm.SelectedPlayer.Color = v;
    }

    private void CommitStartPos()
    {
        if (_suppressCallbacks || _vm == null) return;
        if (int.TryParse(_startPosEntry.Text, out var v) && v > 0)
            _vm.SelectedPlayer.StartPosition = v;
    }

    private void CommitTeam()
    {
        if (_suppressCallbacks || _vm == null) return;
        if (int.TryParse(_teamEntry.Text, out var v) && v > 0)
            _vm.SelectedPlayer.Team = v;
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private static StackPanel HStack(params Control[] controls)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var control in controls)
        {
            stack.Children.Add(control);
        }

        return stack;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Foreground = SectionBrush,
        Margin = new Thickness(0, 8, 0, 2),
    };

    private static TextBlock PlainLabel(string text) => new()
    {
        Text = text,
        Foreground = FieldBrush,
    };

    private static Control FieldRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80") };
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

    private static void StyleEntry(TextBox entry)
    {
        entry.Foreground = FieldBrush;
        entry.Background = FieldBackground;
    }

    private static TextBox NumericEntry(TextBox entry)
    {
        StyleEntry(entry);
        return entry;
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.Foreground = FieldBrush;
        combo.Background = FieldBackground;
    }
}
