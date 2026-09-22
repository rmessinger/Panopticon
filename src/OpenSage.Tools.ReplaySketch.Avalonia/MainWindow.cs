using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenSage.Tools.ReplaySketch.Avalonia.Services;
using OpenSage.Tools.ReplaySketch.Avalonia.ViewModels;
using OpenSage.Tools.ReplaySketch.Avalonia.Views;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Avalonia;

/// <summary>
/// Application shell: three-column work area (scenario editor, tabbed centre, detail panel)
/// with Tree / Map / Validation Ground / Model tabs and the export strip. Screenshot-and-exit
/// mode mirrors the previous MAUI front-end's environment variables:
/// REPLAYSKETCH_SCREENSHOT_PATH (activates), REPLAYSKETCH_SCREENSHOT_TAB
/// (tree|map|validationground|model), REPLAYSKETCH_SCREENSHOT_DELAY (seconds, default 3).
/// </summary>
public sealed class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly Button _treeTabBtn = TabButton("Tree");
    private readonly Button _mapTabBtn = TabButton("Map");
    private readonly Button _validationGroundTabBtn = TabButton("Validation Ground");
    private readonly Button _modelTabBtn = TabButton("Model");
    private readonly Border _treeFrame = new();
    private readonly Border _mapFrame = new() { IsVisible = false };
    private readonly Border _validationGroundFrame = new() { IsVisible = false };
    private readonly Border _modelFrame = new() { IsVisible = false };

    private readonly ScenarioView _scenarioView = new();
    private readonly ActionTreeView _actionTreeView = new();
    private readonly ActionDetailView _actionDetail = new();
    private readonly MapPreviewView _mapPreviewView = new();
    private readonly ValidationGroundEditorView _validationGroundEditorView = new();
    private readonly ExportView _exportView = new();
    private readonly ExternalModelView _externalModelView = new();
    private bool _productionModelViewModelBound;

    private static readonly string? ScreenshotPath =
        Environment.GetEnvironmentVariable("REPLAYSKETCH_SCREENSHOT_PATH");

    private static readonly int ScreenshotDelaySec =
        int.TryParse(Environment.GetEnvironmentVariable("REPLAYSKETCH_SCREENSHOT_DELAY"), out var d) ? d : 3;

    private bool _screenshotScheduled;

    public MainWindow()
    {
        Title = "OpenSAGE Replay Sketch";
        Width = 1280;
        Height = 800;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        _treeTabBtn.Click += (_, _) => SwitchToTree();
        _mapTabBtn.Click += (_, _) => SwitchToMap();
        _validationGroundTabBtn.Click += (_, _) => SwitchToValidationGround();
        _modelTabBtn.Click += (_, _) => SwitchToModel();

        _scenarioView.BindViewModel(_vm);
        _actionTreeView.BindViewModel(_vm);
        _actionTreeView.SetExternalDetailPanel(_actionDetail.ContentPanel);
        _mapPreviewView.BindViewModel(_vm);
        _mapPreviewView.SetActionTreeBridge(_actionTreeView);
        _validationGroundEditorView.BindViewModel(_vm);
        _exportView.BindViewModel(_vm);

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Map)
                                or nameof(MainViewModel.SelectedSlotIndex))
            {
                _actionTreeView.RefreshTree();
                _mapPreviewView.Invalidate();
            }
        };

        _treeFrame.Child = _actionTreeView;
        _mapFrame.Child = _mapPreviewView;
        _validationGroundFrame.Child = _validationGroundEditorView;
        _modelFrame.Child = _externalModelView;

        var tabContent = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var tabBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
        };
        tabBar.Children.Add(_treeTabBtn);
        tabBar.Children.Add(_mapTabBtn);
        tabBar.Children.Add(_validationGroundTabBtn);
        tabBar.Children.Add(_modelTabBtn);

        var frames = new Grid();
        frames.Children.Add(_treeFrame);
        frames.Children.Add(_mapFrame);
        frames.Children.Add(_validationGroundFrame);
        frames.Children.Add(_modelFrame);

        tabContent.Children.Add(tabBar);
        Grid.SetRow(frames, 1);
        tabContent.Children.Add(frames);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*,300"), Margin = new Thickness(4, 0) };
        var leftBorder = PanelBorder(_scenarioView);
        leftBorder.SetValue(Grid.ColumnProperty, 0);
        tabContent.SetValue(Grid.ColumnProperty, 1);
        var rightBorder = PanelBorder(_actionDetail);
        rightBorder.SetValue(Grid.ColumnProperty, 2);
        columns.Children.Add(leftBorder);
        columns.Children.Add(tabContent);
        columns.Children.Add(rightBorder);

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        columns.SetValue(Grid.RowProperty, 0);
        var exportBorder = PanelBorder(_exportView);
        exportBorder.SetValue(Grid.RowProperty, 1);
        exportBorder.Margin = new Thickness(4, 2);
        root.Children.Add(columns);
        root.Children.Add(exportBorder);

        Content = root;

        Opened += OnOpened;
    }

    private static Border PanelBorder(Control child) => new()
    {
        BorderBrush = new SolidColorBrush(Color.Parse("#444")),
        BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.Parse("#252525")),
        Margin = new Thickness(2),
        CornerRadius = new CornerRadius(4),
        Child = child,
    };

    private static Button TabButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(16, 6),
        Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
        Foreground = new SolidColorBrush(Color.Parse("#888888")),
    };

    private void OnOpened(object? sender, EventArgs e)
    {
        StorageProviderSource.Register(this);
        ReplaySketchLog.Write("MainWindow.Opened");
        _vm.Initialize();

        if (ScreenshotPath is not null && !_screenshotScheduled)
        {
            _screenshotScheduled = true;
            var tab = Environment.GetEnvironmentVariable("REPLAYSKETCH_SCREENSHOT_TAB")?.ToLowerInvariant();
            if (tab == "map")
                SwitchToMap();
            else if (tab == "validationground")
                SwitchToValidationGround();
            else if (tab == "model")
                SwitchToModel(useScreenshotFixture: true);
            DispatcherTimer.RunOnce(() => _ = TakeScreenshotAsync(), TimeSpan.FromSeconds(ScreenshotDelaySec));
        }
    }

    private async Task TakeScreenshotAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(ScreenshotPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Two layout passes so async-loaded content settles before capture.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(200);

            var pixelSize = new PixelSize(
                Math.Max((int)Bounds.Width, 1),
                Math.Max((int)Bounds.Height, 1));
            using var bitmap = new RenderTargetBitmap(pixelSize);
            bitmap.Render(this);
            bitmap.Save(ScreenshotPath!);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(ScreenshotPath! + ".error", ex.ToString());
        }
        finally
        {
            if (Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void SwitchToTree()
    {
        _treeFrame.IsVisible = true;
        _mapFrame.IsVisible = false;
        _validationGroundFrame.IsVisible = false;
        _modelFrame.IsVisible = false;
        SetTabActive(_treeTabBtn, true);
        SetTabActive(_mapTabBtn, false);
        SetTabActive(_validationGroundTabBtn, false);
        SetTabActive(_modelTabBtn, false);
    }

    private void SwitchToMap()
    {
        _treeFrame.IsVisible = false;
        _mapFrame.IsVisible = true;
        _validationGroundFrame.IsVisible = false;
        _modelFrame.IsVisible = false;
        SetTabActive(_treeTabBtn, false);
        SetTabActive(_mapTabBtn, true);
        SetTabActive(_validationGroundTabBtn, false);
        SetTabActive(_modelTabBtn, false);
        _mapPreviewView.Invalidate();
    }

    private void SwitchToValidationGround()
    {
        _treeFrame.IsVisible = false;
        _mapFrame.IsVisible = false;
        _validationGroundFrame.IsVisible = true;
        _modelFrame.IsVisible = false;
        SetTabActive(_treeTabBtn, false);
        SetTabActive(_mapTabBtn, false);
        SetTabActive(_validationGroundTabBtn, true);
        SetTabActive(_modelTabBtn, false);
        _validationGroundEditorView.Invalidate();
    }

    private void SwitchToModel(bool useScreenshotFixture = false)
    {
        _treeFrame.IsVisible = false;
        _mapFrame.IsVisible = false;
        _validationGroundFrame.IsVisible = false;
        _modelFrame.IsVisible = true;
        SetTabActive(_treeTabBtn, false);
        SetTabActive(_mapTabBtn, false);
        SetTabActive(_validationGroundTabBtn, false);
        SetTabActive(_modelTabBtn, true);

        if (useScreenshotFixture)
        {
            // Deterministic fake state; no TestHarness process is launched.
            _externalModelView.Bind(ExternalModelScreenshotFixture.Create());
            return;
        }

        if (!_productionModelViewModelBound)
        {
            _productionModelViewModelBound = true;
            var artifactStore = new ExternalModelArtifactStore();
            _externalModelView.Bind(new global::OpenSage.Tools.ReplaySketch.ViewModels.ExternalModelViewModel(
                new ExternalModelSession(new ProductionMountFactory(), artifactStore),
                artifactStore,
                new AvaloniaExternalModelFileSelector(() => StorageProviderSource.Current),
                new AvaloniaMainThreadDispatcher(),
                new TestHarnessPreviewClient()));
        }
    }

    private static void SetTabActive(Button btn, bool active)
    {
        btn.Background = new SolidColorBrush(active ? Color.Parse("#252525") : Color.Parse("#2A2A2A"));
        btn.Foreground = new SolidColorBrush(active ? Color.Parse("#EEE") : Color.Parse("#888888"));
    }

    /// <summary>
    /// Production mount factory over the engine's ExternalAssetSourceMount.
    /// </summary>
    private sealed class ProductionMountFactory : IExternalAssetSourceMountFactory
    {
        public IExternalSourceMount Create(string absoluteSelectedFile, Model.ExternalModelImportOptions options)
        {
            var mount = new OpenSage.Content.Importing.ExternalAssetSourceMount(
                absoluteSelectedFile,
                new OpenSage.Content.Importing.Gltf.GltfImportOptions(unitsPerMeter: options.UnitsPerMeter));
            return new EngineMount(mount);
        }
    }

    private sealed class EngineMount(OpenSage.Content.Importing.ExternalAssetSourceMount mount)
        : IExternalSourceMount
    {
        public string RootVirtualKey => mount.RootVirtualKey;

        public OpenSage.Content.Importing.ExternalFormatSelection FormatSelection => mount.FormatSelection;

        public OpenSage.Content.Importing.AssetImportContext ImportContext => mount.ImportContext;

        public void Dispose() => mount.Dispose();
    }
}
