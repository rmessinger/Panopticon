using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenSage.Tools.ReplaySketch.Avalonia.ViewModels;

namespace OpenSage.Tools.ReplaySketch.Avalonia.Views;

/// <summary>
/// Bottom strip: validate, export, and play-in-game commands with a shared result label.
/// Ported from the MAUI ExportView.
/// </summary>
public sealed class ExportView : UserControl
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush FailBrush = new SolidColorBrush(Color.Parse("#F44747"));

    private MainViewModel? _vm;

    private readonly Button _validateButton = new() { Content = "Validate", Width = 110 };
    private readonly Button _exportButton = new() { Content = "Export to Replays Dir", Width = 170 };
    private readonly Button _playButton = new() { Content = "\u25B6  Play in Game", Width = 150 };
    private readonly TextBlock _resultLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };

    public ExportView()
    {
        _playButton.Background = new SolidColorBrush(Color.Parse("#388E3C"));
        _playButton.Foreground = Brushes.White;

        _validateButton.Click += OnValidateClicked;
        _exportButton.Click += OnExportClicked;
        _playButton.Click += OnPlayClicked;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(_validateButton);
        panel.Children.Add(_exportButton);
        panel.Children.Add(_playButton);
        panel.Children.Add(_resultLabel);

        Content = panel;
    }

    public void BindViewModel(MainViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.MapLoaded)
                                or nameof(MainViewModel.ExportResult)
                                or nameof(MainViewModel.ValidationResult))
                Refresh();
        };
        Refresh();
    }

    private void Refresh()
    {
        if (_vm == null) return;

        _validateButton.IsEnabled = _vm.MapLoaded;
        _exportButton.IsEnabled = _vm.MapLoaded;

        var launcherFound = _vm.LauncherPath != null;
        _playButton.IsEnabled = _vm.MapLoaded && launcherFound;
        global::Avalonia.Controls.ToolTip.SetTip(_playButton, launcherFound
            ? "Export sketch_preview.rep and launch OpenSage.Launcher"
            : "OpenSage.Launcher not found in sibling build dirs");

        var text = _vm.ValidationResult ?? _vm.ExportResult;
        if (text != null)
        {
            var ok = (_vm.ValidationResult != null && _vm.ValidationOk) ||
                     (_vm.ExportResult != null && _vm.ExportOk);
            _resultLabel.Text = text;
            _resultLabel.Foreground = ok ? OkBrush : FailBrush;
        }
        else
        {
            _resultLabel.Text = string.Empty;
        }
    }

    private void OnValidateClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm?.ValidateCommand.Execute(null);
        Refresh();
    }

    private void OnExportClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm?.ExportCommand.Execute(null);
        Refresh();
    }

    private void OnPlayClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm?.PlayInGameCommand.Execute(null);
        Refresh();
    }
}
