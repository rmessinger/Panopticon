using Microsoft.Maui.Media;
using OpenSage.Tools.ReplaySketch.Maui.ViewModels;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Maui;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm = new();

    // Screenshot-and-exit mode. Set environment variables before launching:
    //   REPLAYSKETCH_SCREENSHOT_PATH=/tmp/shot.png   (required to activate)
    //   REPLAYSKETCH_SCREENSHOT_TAB=tree|map|validationground  (optional, default: tree)
    //   REPLAYSKETCH_SCREENSHOT_DELAY=N              (optional seconds, default: 3)
    private static readonly string? ScreenshotPath =
        Environment.GetEnvironmentVariable("REPLAYSKETCH_SCREENSHOT_PATH");

    private static readonly int ScreenshotDelaySec =
        int.TryParse(Environment.GetEnvironmentVariable("REPLAYSKETCH_SCREENSHOT_DELAY"), out var d) ? d : 3;

    private bool _screenshotScheduled;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        ScenarioView.BindViewModel(_vm);
        ActionTreeView.BindViewModel(_vm);
        ActionTreeView.SetExternalDetailPanel(ActionDetail.ContentPanel);
        MapPreviewView.BindViewModel(_vm);
        MapPreviewView.SetActionTreeBridge(ActionTreeView);
        ValidationGroundEditorView.BindViewModel(_vm);
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

    private void OnLoaded(object? sender, EventArgs e)
    {
        ReplaySketchLog.Write("MainPage.Loaded");
        _vm.Initialize();

        if (ScreenshotPath is not null && !_screenshotScheduled)
        {
            _screenshotScheduled = true;
            var tab = Environment.GetEnvironmentVariable("REPLAYSKETCH_SCREENSHOT_TAB")?.ToLowerInvariant();
            if (tab == "map")
                SwitchToMap();
            else if (tab == "validationground")
                SwitchToValidationGround();
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(ScreenshotDelaySec), () => _ = TakeScreenshotAsync());
        }
    }

    private async Task TakeScreenshotAsync()
    {
        try
        {
            if (!Screenshot.Default.IsCaptureSupported)
            {
                await File.WriteAllTextAsync(ScreenshotPath! + ".error",
                    "Screenshot capture is not supported on this platform.");
            }
            else
            {
                var dir = Path.GetDirectoryName(ScreenshotPath!);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var result = await Screenshot.Default.CaptureAsync();
                await using var stream = await result.OpenReadAsync();
                await using var output = File.Create(ScreenshotPath!);
                await stream.CopyToAsync(output);
            }
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(ScreenshotPath! + ".error", ex.ToString());
        }
        finally
        {
            Application.Current?.Quit();
        }
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void OnTreeTabClicked(object? sender, EventArgs e) => SwitchToTree();
    private void OnMapTabClicked(object? sender, EventArgs e) => SwitchToMap();
    private void OnValidationGroundTabClicked(object? sender, EventArgs e) => SwitchToValidationGround();

    private void SwitchToTree()
    {
        TreeFrame.IsVisible = true;
        MapFrame.IsVisible = false;
        ValidationGroundFrame.IsVisible = false;
        SetTabActive(TreeTabBtn, true);
        SetTabActive(MapTabBtn, false);
        SetTabActive(ValidationGroundTabBtn, false);
    }

    private void SwitchToMap()
    {
        TreeFrame.IsVisible = false;
        MapFrame.IsVisible = true;
        ValidationGroundFrame.IsVisible = false;
        SetTabActive(TreeTabBtn, false);
        SetTabActive(MapTabBtn, true);
        SetTabActive(ValidationGroundTabBtn, false);
        MapPreviewView.Invalidate();
    }

    private void SwitchToValidationGround()
    {
        TreeFrame.IsVisible = false;
        MapFrame.IsVisible = false;
        ValidationGroundFrame.IsVisible = true;
        SetTabActive(TreeTabBtn, false);
        SetTabActive(MapTabBtn, false);
        SetTabActive(ValidationGroundTabBtn, true);
        ValidationGroundEditorView.Invalidate();
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
        ReplaySketchLog.Write("MainPage.OnAppearing");
        _vm.Initialize();
    }
}
