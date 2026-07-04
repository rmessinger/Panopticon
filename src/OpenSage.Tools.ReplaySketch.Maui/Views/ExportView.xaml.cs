using OpenSage.Tools.ReplaySketch.Maui.ViewModels;

namespace OpenSage.Tools.ReplaySketch.Maui.Views;

public partial class ExportView : ContentView
{
    private MainViewModel? _vm;

    public ExportView()
    {
        InitializeComponent();
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

        ValidateButton.IsEnabled = _vm.MapLoaded;
        ExportButton.IsEnabled = _vm.MapLoaded;

        var launcherFound = _vm.LauncherPath != null;
        PlayButton.IsEnabled = _vm.MapLoaded && launcherFound;
        ToolTipProperties.SetText(PlayButton, launcherFound
            ? "Export sketch_preview.rep and launch OpenSage.Launcher"
            : "OpenSage.Launcher not found in sibling build dirs");

        // Show whichever result is most recent
        var text = _vm.ValidationResult ?? _vm.ExportResult;
        if (text != null)
        {
            var ok = (_vm.ValidationResult != null && _vm.ValidationOk) ||
                     (_vm.ExportResult != null && _vm.ExportOk);
            ResultLabel.Text = text;
            ResultLabel.TextColor = ok ? Color.FromArgb("#4EC9B0") : Color.FromArgb("#F44747");
        }
        else
        {
            ResultLabel.Text = string.Empty;
        }
    }

    private void OnValidateClicked(object? sender, EventArgs e)
    {
        _vm?.ValidateCommand.Execute(null);
        Refresh();
    }

    private void OnExportClicked(object? sender, EventArgs e)
    {
        _vm?.ExportCommand.Execute(null);
        Refresh();
    }

    private void OnPlayClicked(object? sender, EventArgs e)
    {
        _vm?.PlayInGameCommand.Execute(null);
        Refresh();
    }
}
