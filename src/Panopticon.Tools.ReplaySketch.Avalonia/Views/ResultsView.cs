using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Panopticon.Tools.ReplaySketch.Avalonia.Controls;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Profiles;
using Panopticon.Tools.ReplaySketch.ViewModels;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Views;

/// <summary>
/// WI-A5-04: results replayer view. Thin shell (Avalonia/AGENTS.md): every behavior lives in
/// <see cref="ResultsViewModel"/> and <see cref="RunBundleTimeline"/>; this view only binds
/// controls to them and paints the chart through the shared Skia canvas.
/// </summary>
public sealed class ResultsView : UserControl
{
    private readonly ResultsViewModel _viewModel;
    private readonly TextBox _path = new() { MinWidth = 260, Watermark = "run.json path" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _series = new() { MinWidth = 200 };
    private readonly SkiaCanvas _chart = new();
    private readonly Slider _timeline = new() { MinWidth = 220 };
    private readonly TextBox _frame = new() { MinWidth = 60 };
    private readonly TextBlock _currentState = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _events = new();
    private readonly TextBox _comparePath = new();
    private readonly TextBlock _divergence = new();
    private bool _refreshing;

    public ResultsView(ResultsViewModel viewModel)
    {
        _viewModel = viewModel;

        _path.TextChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _viewModel.BundlePath = _path.Text ?? string.Empty;
            }
        };

        _series.ItemTemplate = new FuncDataTemplate<MetricSeriesRecord>((item, _) => new TextBlock
        {
            Text = item is null ? string.Empty : $"{item.Name} ({item.Points.Count})",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4),
        });
        _series.SelectionChanged += (_, _) => _chart.InvalidateCanvas();

        _timeline.ValueChanged += (_, e) =>
        {
            if (!_refreshing)
            {
                _viewModel.ScrubTo((uint)e.NewValue);
            }
        };

        _frame.TextChanged += (_, _) =>
        {
            if (!_refreshing
                && uint.TryParse(_frame.Text, out var frame))
            {
                _viewModel.ScrubTo(frame);
            }
        };

        _events.ItemTemplate = new FuncDataTemplate<RunEventRecord>((item, _) => new TextBlock
        {
            Text = item is null
                ? string.Empty
                : $"{item.Frame}: {item.Category} {(string.IsNullOrEmpty(item.ActionType) ? string.Empty : item.ActionType + " ")}{item.PlayerName ?? string.Empty} {item.DefinitionName ?? string.Empty}".TrimEnd(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4),
        });
        _events.SelectionChanged += (_, _) =>
        {
            if (_events.SelectedItem is RunEventRecord record && !_refreshing)
            {
                _viewModel.ScrubTo(record.Frame);
            }
        };

        _chart.PaintSurface += OnPaintSurface;

        _viewModel.PropertyChanged += (_, _) => Refresh();
        _viewModel.PropertyChanged += (_, _) => _chart.InvalidateCanvas();

        var browse = new Button { Content = "Browse…", Command = _viewModel.BrowseCommand };
        var load = new Button { Content = "Load", Command = _viewModel.LoadCommand };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toolbar.Children.Add(_path);
        toolbar.Children.Add(browse);
        toolbar.Children.Add(load);
        toolbar.Children.Add(_status);

        // WI-A5-05: divergence comparison against a second run manifest.
        _comparePath.Watermark = "second run's run.json";
        _comparePath.MinWidth = 220;
        _comparePath.TextChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _viewModel.ComparePath = _comparePath.Text;
            }
        };
        var compare = new Button { Content = "Compare", Command = _viewModel.CompareCommand };
        _divergence.TextWrapping = TextWrapping.Wrap;
        _divergence.FontFamily = new FontFamily("monospace");
        var compareRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        compareRow.Children.Add(_comparePath);
        compareRow.Children.Add(compare);
        compareRow.Children.Add(_divergence);

        var header = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(compareRow, 1);
        header.Children.Add(toolbar);
        header.Children.Add(compareRow);

        var chartPanel = new Grid { RowDefinitions = new RowDefinitions("260,*") };
        Grid.SetRow(_chart, 1);
        chartPanel.Children.Add(_series);
        chartPanel.Children.Add(_chart);

        var timelineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        timelineRow.Children.Add(_timeline);
        timelineRow.Children.Add(_frame);
        timelineRow.Children.Add(_currentState);

        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(timelineRow, 0);
        right.Children.Add(timelineRow);
        _events.FontFamily = new FontFamily("monospace");
        Grid.SetRow(_events, 1);
        right.Children.Add(_events);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*") };
        Grid.SetColumn(chartPanel, 0);
        Grid.SetColumn(right, 1);
        body.Children.Add(chartPanel);
        body.Children.Add(right);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 2);
        root.Children.Add(header);
        root.Children.Add(body);

        Content = root;
        Refresh();
    }

    private void OnPaintSurface(object? sender, SKCanvas canvas)
    {
        canvas.Clear();

        if (!_viewModel.HasTimeline)
        {
            return;
        }

        var series = _series.SelectedItem is MetricSeriesRecord selected
            ? [(selected.Name, selected.Points)]
            : _viewModel.Series.Select(s => (s.Name, s.Points)).ToList();
        MetricChartRenderer.Render(
            canvas,
            offsetX: 0,
            offsetY: 0,
            width: (float)_chart.Bounds.Width,
            height: (float)_chart.Bounds.Height,
            series);
    }

    private void Refresh()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            if (!string.Equals(_path.Text, _viewModel.BundlePath, StringComparison.Ordinal))
            {
                _path.Text = _viewModel.BundlePath ?? string.Empty;
            }

            _status.Text = _viewModel.StatusMessage ?? string.Empty;
            _currentState.Text = _viewModel.StatusMessage ?? string.Empty;
            _series.ItemsSource = _viewModel.Series;
            _events.ItemsSource = _viewModel.EventsThroughFrame;
            _timeline.IsEnabled = _viewModel.HasTimeline;
            if (_viewModel.HasTimeline)
            {
                _timeline.Minimum = _viewModel.Timeline!.FirstFrame;
                _timeline.Maximum = _viewModel.Timeline.LastFrame;
                _timeline.Value = _viewModel.SelectedFrame;
            }

            if (!string.Equals(_frame.Text, _viewModel.SelectedFrame.ToString(), StringComparison.Ordinal))
            {
                _frame.Text = _viewModel.SelectedFrame.ToString();
            }

            if (!string.Equals(_comparePath.Text, _viewModel.ComparePath, StringComparison.Ordinal))
            {
                _comparePath.Text = _viewModel.ComparePath ?? string.Empty;
            }

            _divergence.Text = string.Join(Environment.NewLine, _viewModel.DivergenceLines);
        }
        finally
        {
            _refreshing = false;
        }
    }
}
