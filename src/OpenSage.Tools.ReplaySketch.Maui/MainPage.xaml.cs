using OpenSage.Tools.ReplaySketch.Maui.ViewModels;

namespace OpenSage.Tools.ReplaySketch.Maui;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm = new();
    private bool _initialized;

    public MainPage()
    {
        InitializeComponent();

        ScenarioView.BindViewModel(_vm);
        ActionTreeView.BindViewModel(_vm);
        ActionTreeView.SetExternalDetailPanel(ActionDetail.ContentPanel);
        MapPreviewView.BindViewModel(_vm);
        MapPreviewView.SetActionTreeBridge(ActionTreeView);
        ExportView.BindViewModel(_vm);

        // When scenario or map changes, refresh the action tree and map
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Map)
                                or nameof(MainViewModel.SelectedSlotIndex))
            {
                ActionTreeView.RefreshTree();
                MapPreviewView.Invalidate();
            }
        };
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void OnTreeTabClicked(object? sender, EventArgs e) => SwitchToTree();
    private void OnMapTabClicked(object? sender, EventArgs e) => SwitchToMap();

    private void SwitchToTree()
    {
        TreeFrame.IsVisible = true;
        MapFrame.IsVisible = false;
        SetTabActive(TreeTabBtn, true);
        SetTabActive(MapTabBtn, false);
    }

    private void SwitchToMap()
    {
        TreeFrame.IsVisible = false;
        MapFrame.IsVisible = true;
        SetTabActive(TreeTabBtn, false);
        SetTabActive(MapTabBtn, true);
        MapPreviewView.Invalidate();
    }

    private static void SetTabActive(Button btn, bool active)
    {
        btn.BackgroundColor = active
            ? Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#252525")
                : Color.FromArgb("#FAFAFA")
            : Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#2A2A2A")
                : Color.FromArgb("#E0E0E0");
        btn.TextColor = active
            ? Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#EEE")
                : Color.FromArgb("#333")
            : Color.FromArgb("#888");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _ = _vm.InitializeAsync();
    }
}
