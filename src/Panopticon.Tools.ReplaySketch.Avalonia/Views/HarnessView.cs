using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.ViewModels;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Views;

public sealed class HarnessView : UserControl
{
    private readonly HarnessLauncherViewModel _viewModel;
    private readonly TextBox _filter = new() { PlaceholderText = "Filter scenarios", MinWidth = 180 };
    private readonly ComboBox _mode = new();
    private readonly ComboBox _category = new();
    private readonly ListBox _scenarios = new();
    private readonly StackPanel _parameters = new() { Spacing = 6 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("monospace") };
    private readonly ComboBox _presets = new() { MinWidth = 140, PlaceholderText = "Presets" };
    private readonly TextBox _presetName = new() { PlaceholderText = "Preset name", Width = 140 };
    private IReadOnlyList<HarnessParameterRow>? _renderedRows;
    private bool _refreshing;

    public HarnessView(HarnessLauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        _filter.TextChanged += (_, _) => { if (!_refreshing) _viewModel.FilterText = _filter.Text ?? string.Empty; };
        _mode.ItemsSource = new[] { HarnessRunModes.None, HarnessRunModes.Headless, HarnessRunModes.Windowed };
        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) => { if (!_refreshing) _viewModel.ModeFilter = _mode.SelectedItem is HarnessRunModes mode ? mode : HarnessRunModes.None; };
        _category.ItemsSource = new[] { "All categories" }.Concat(Enum.GetNames<HarnessScenarioCategory>()).ToArray();
        _category.SelectedIndex = 0;
        _category.SelectionChanged += (_, _) => { if (!_refreshing) _viewModel.CategoryFilter = _category.SelectedIndex > 0 ? Enum.Parse<HarnessScenarioCategory>((string)_category.SelectedItem!) : null; };
        _scenarios.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<HarnessScenarioDescriptor>((scenario, _) => new TextBlock
        {
            Text = scenario is null ? string.Empty : $"{scenario.Id} [{scenario.Modes}]\n{scenario.Summary}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4),
        });
        _scenarios.SelectionChanged += (_, _) => { if (!_refreshing) _viewModel.SelectedScenario = _scenarios.SelectedItem as HarnessScenarioDescriptor; };
        _viewModel.PropertyChanged += (_, _) => Refresh();

        var filters = new StackPanel { Spacing = 6 };
        filters.Children.Add(_filter);
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        choices.Children.Add(_category);
        choices.Children.Add(_mode);
        filters.Children.Add(choices);
        filters.Children.Add(new Button { Content = "Reload catalog", Command = _viewModel.LoadCatalogCommand });
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(6) };
        left.Children.Add(filters);
        Grid.SetRow(_scenarios, 1);
        left.Children.Add(_scenarios);

        var presets = new WrapPanel { Orientation = Orientation.Horizontal };
        presets.Children.Add(_presets);
        presets.Children.Add(Button("Load", () => { if (_presets.SelectedItem is string name) _viewModel.LoadPreset(name); }));
        presets.Children.Add(Button("Delete", () => { if (_presets.SelectedItem is string name) _viewModel.DeletePreset(name); }));
        presets.Children.Add(_presetName);
        presets.Children.Add(Button("Save", () => _viewModel.SavePreset(_presetName.Text ?? string.Empty)));
        var import = new Button { Content = "Import config", Padding = new Thickness(10, 4) };
        import.Click += async (_, _) => await ImportAsync();
        presets.Children.Add(import);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(new Button { Content = "Run", Command = _viewModel.RunCommand });
        actions.Children.Add(new Button { Content = "Stop", Command = _viewModel.StopCommand });
        actions.Children.Add(_status);
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,160"), Margin = new Thickness(6) };
        right.Children.Add(new TextBlock { Text = "Parameters", FontWeight = FontWeight.Bold });
        var scroll = new ScrollViewer { Content = _parameters };
        Grid.SetRow(scroll, 1);
        right.Children.Add(scroll);
        Grid.SetRow(presets, 2);
        right.Children.Add(presets);
        Grid.SetRow(actions, 3);
        right.Children.Add(actions);
        Grid.SetRow(_output, 4);
        right.Children.Add(_output);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("350,*") };
        root.Children.Add(left);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        Content = root;
    }

    public async Task LoadAsync()
    {
        try
        {
            await _viewModel.LoadCatalogCommand.ExecuteAsync();
            _viewModel.SelectedScenario ??= _viewModel.VisibleScenarios.FirstOrDefault(scenario => scenario.Id == "UnitValidationGround");
            Refresh();
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 4) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception error) { _status.Text = error.Message; }
        };
        return button;
    }

    private async Task<string?> PickFileAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task ImportAsync()
    {
        try
        {
            if (await PickFileAsync() is { } path) _viewModel.ImportConfig(path);
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private Control Editor(HarnessParameterRow row)
    {
        var binding = new Binding(nameof(HarnessParameterRow.Text)) { Source = row, Mode = BindingMode.TwoWay };
        if (row.Descriptor.Kind == HarnessParameterKind.Bool)
        {
            var check = new CheckBox { IsChecked = bool.TryParse(row.Text, out var value) && value, IsEnabled = !row.IsLocked };
            check.IsCheckedChanged += (_, _) => row.Text = check.IsChecked == true ? "true" : "false";
            row.PropertyChanged += (_, _) => check.IsChecked = bool.TryParse(row.Text, out var current) && current;
            return check;
        }

        if (row.Descriptor.Kind == HarnessParameterKind.Enum)
        {
            var combo = new ComboBox { ItemsSource = row.Descriptor.Default is null ? new[] { string.Empty }.Concat(row.Descriptor.EnumValues) : row.Descriptor.EnumValues, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Bind(SelectingItemsControl.SelectedItemProperty, binding);
            return combo;
        }

        if (row.Descriptor.Name == "ScenarioPath" && _viewModel.SelectedScenario?.EmbeddedProfiles.Count > 0)
        {
            var suggestions = new AutoCompleteBox { ItemsSource = _viewModel.SelectedScenario.EmbeddedProfiles };
            suggestions.Bind(AutoCompleteBox.TextProperty, binding);
            return suggestions;
        }

        var box = new TextBox { IsReadOnly = row.IsLocked, AcceptsReturn = row.Descriptor.Kind == HarnessParameterKind.Json, MinHeight = row.Descriptor.Kind == HarnessParameterKind.Json ? 80 : 0 };
        box.Bind(TextBox.TextProperty, binding);
        if (row.Descriptor.Kind != HarnessParameterKind.Path) return box;
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        panel.Children.Add(box);
        var browse = new Button { Content = "Browse" };
        browse.Click += async (_, _) =>
        {
            try { if (await PickFileAsync() is { } path) row.Text = path; }
            catch (Exception error) { _status.Text = error.Message; }
        };
        Grid.SetColumn(browse, 1);
        panel.Children.Add(browse);
        return panel;
    }

    private void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            _filter.Text = _viewModel.FilterText;
            _mode.SelectedItem = _viewModel.ModeFilter;
            _category.SelectedItem = _viewModel.CategoryFilter?.ToString() ?? "All categories";
            if (!ReferenceEquals(_scenarios.ItemsSource, _viewModel.VisibleScenarios)) _scenarios.ItemsSource = _viewModel.VisibleScenarios;
            _scenarios.SelectedItem = _viewModel.SelectedScenario;
            // Keep controls and focus stable while validation and output notifications arrive.
            if (!ReferenceEquals(_renderedRows, _viewModel.Parameters.Rows))
            {
                _renderedRows = _viewModel.Parameters.Rows;
                _parameters.Children.Clear();
                foreach (var group in _viewModel.Parameters.Groups)
                {
                    _parameters.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeight.Bold, Foreground = Brushes.LightSkyBlue });
                    foreach (var row in group)
                    {
                        var label = new TextBlock();
                        void UpdateLabel() => label.Text = row.Descriptor.Name + (row.IsOverridden ? " (modified)" : string.Empty);
                        UpdateLabel();
                        row.PropertyChanged += (_, _) => UpdateLabel();
                        var error = new TextBlock { Foreground = Brushes.IndianRed };
                        error.Bind(TextBlock.TextProperty, new Binding(nameof(HarnessParameterRow.Error)) { Source = row });
                        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        header.Children.Add(label);
                        header.Children.Add(Button("Reset", row.Reset));
                        var panel = new StackPanel { Children = { header, Editor(row), error } };
                        ToolTip.SetTip(panel, row.Descriptor.Description);
                        _parameters.Children.Add(panel);
                    }
                }
            }

            var selectedPreset = _presets.SelectedItem;
            var names = _viewModel.Presets;
            if (_presets.ItemsSource is not IEnumerable<string> previous || !previous.SequenceEqual(names))
            {
                _presets.ItemsSource = names;
                _presets.SelectedItem = selectedPreset;
            }

            _status.Text = _viewModel.StatusMessage ?? (_viewModel.LastResult is { } result ? $"Exit {result.ExitCode}: {result.Meaning}" : string.Empty);
            _output.Text = string.Join(Environment.NewLine, _viewModel.Output);
        }
        finally { _refreshing = false; }
    }
}
