using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.ViewModels;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Views;

public sealed class HarnessView : UserControl
{
    private readonly HarnessLauncherViewModel _viewModel;
    private readonly TextBox _filter = new() { Watermark = "Filter scenarios" };
    private readonly ComboBox _mode = new();
    private readonly ListBox _scenarios = new();
    private readonly StackPanel _parameters = new() { Spacing = 4 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = FontFamily.Default };

    public HarnessView(HarnessLauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        _filter.TextChanged += (_, _) => _viewModel.FilterText = _filter.Text ?? string.Empty;
        _mode.ItemsSource = new[] { HarnessRunModes.None, HarnessRunModes.Headless, HarnessRunModes.Windowed };
        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) => _viewModel.ModeFilter = _mode.SelectedItem is HarnessRunModes mode ? mode : HarnessRunModes.None;
        _scenarios.SelectionChanged += (_, _) => _viewModel.SelectedScenario = _scenarios.SelectedItem as HarnessScenarioDescriptor;
        _viewModel.PropertyChanged += (_, _) => Refresh();

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        filters.Children.Add(_filter); filters.Children.Add(_mode);
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(6) };
        left.Children.Add(filters); Grid.SetRow(_scenarios, 1); left.Children.Add(_scenarios);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(Button("Run", () => ((System.Windows.Input.ICommand)_viewModel.RunCommand).Execute(null)));
        actions.Children.Add(Button("Stop", () => ((System.Windows.Input.ICommand)_viewModel.StopCommand).Execute(null)));
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,160"), Margin = new Thickness(6) };
        right.Children.Add(new TextBlock { Text = "Parameters", FontWeight = FontWeight.Bold });
        var scroll = new ScrollViewer { Content = _parameters }; Grid.SetRow(scroll, 1); right.Children.Add(scroll);
        Grid.SetRow(actions, 2); right.Children.Add(actions); Grid.SetRow(_output, 3); right.Children.Add(_output);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*") }; root.Children.Add(left); Grid.SetColumn(right, 1); root.Children.Add(right);
        Content = root;
    }

    public async Task LoadAsync()
    {
        await _viewModel.LoadCatalogCommand.ExecuteAsync();
        _viewModel.SelectedScenario ??= _viewModel.VisibleScenarios.FirstOrDefault(scenario => scenario.Id == "UnitValidationGround");
        Refresh();
    }

    private static Button Button(string text, Action action) { var button = new Button { Content = text, Padding = new Thickness(10, 4) }; button.Click += (_, _) => action(); return button; }
    private void Refresh()
    {
        _scenarios.ItemsSource = _viewModel.VisibleScenarios;
        _scenarios.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<HarnessScenarioDescriptor>((scenario, _) => new TextBlock { Text = scenario is null ? string.Empty : $"{scenario.Id}\n{scenario.Summary}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        _scenarios.SelectedItem = _viewModel.SelectedScenario;
        _parameters.Children.Clear();
        foreach (var group in _viewModel.Parameters.Groups)
        {
            _parameters.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeight.Bold, Foreground = Brushes.LightSkyBlue });
            foreach (var row in group)
            {
                var box = new TextBox { Text = row.Text, IsReadOnly = row.IsLocked, Watermark = row.Descriptor.Description };
                box.TextChanged += (_, _) => row.Text = box.Text ?? string.Empty;
                _parameters.Children.Add(new StackPanel { Children = { new TextBlock { Text = row.Descriptor.Name }, box, new TextBlock { Text = row.Error ?? string.Empty, Foreground = Brushes.IndianRed } } });
            }
        }

        _status.Text = _viewModel.StatusMessage ?? _viewModel.LastResult?.Meaning ?? string.Empty;
        _output.Text = string.Join(Environment.NewLine, _viewModel.Output);
    }
}
