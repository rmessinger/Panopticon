using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using global::Avalonia.Platform.Storage;
using Panopticon.Tools.ReplaySketch.Avalonia.Controls;
using Panopticon.Tools.ReplaySketch.Avalonia.ViewModels;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Profiles;
using Panopticon.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Views;

/// <summary>
/// Bird's-eye map preview with zoom, pan, marker hit-testing, and tap-to-position for the
/// selected action, painted through the Core MapPreviewRenderer. Ported from the MAUI
/// MapPreviewView; mouse/pointer events replace SKTouch and pinch gestures (wheel zoom).
/// </summary>
public sealed class MapPreviewView : UserControl
{
    private MainViewModel? _vm;
    private ActionTreeView? _treeView;

    // Cached bird's-eye terrain thumbnail for the current map
    private SKBitmap? _previewBitmap;
    private MapMetadataService? _previewBitmapSource;

    // Cached base layout — updated every paint
    private float _previewOffsetX;
    private float _previewOffsetY;
    private float _previewSize;

    // Marker hit-test table rebuilt every paint (pre-view-transform coords)
    private readonly List<MarkerInfo> _markers = new();
    private readonly record struct MarkerInfo(int PlayerIdx, string ActionId, SKPoint Screen);
    private const float HitRadiusPx = 14f;

    // ── Zoom / pan ────────────────────────────────────────────────────────────
    private SKMatrix _viewMatrix = SKMatrix.Identity;
    private const float MinScale = 0.5f;
    private const float MaxScale = 10f;

    // Single-pointer drag state
    private bool _pointerCaptured;
    private SKPoint _dragStartTouch;
    private SKMatrix _dragStartMatrix;
    private bool _hasDragged;
    private const float DragThresholdPx = 6f;

    // Double-click reset detection
    private DateTime _lastTapTime = DateTime.MinValue;
    private SKPoint _lastTapPoint;
    private static readonly TimeSpan DoubleTapInterval = TimeSpan.FromMilliseconds(350);
    private const float DoubleTapDistPx = 30f;

    private readonly SkiaCanvas _canvas = new();
    private readonly TextBox _installPathEntry = new();
    private readonly TextBlock _noMapErrorLabel = new() { Text = "No map loaded" };
    private readonly Panel _noMapOverlay = new();
    private readonly Button _resetZoomBtn = new() { Content = "\u21BA Reset", IsVisible = false };

    public MapPreviewView()
    {
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.PointerPressed += OnCanvasPointerPressed;
        _canvas.PointerMoved += OnCanvasPointerMoved;
        _canvas.PointerReleased += OnCanvasPointerReleased;
        _canvas.PointerWheelChanged += OnCanvasWheel;

        _noMapErrorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _noMapErrorLabel.TextWrapping = TextWrapping.Wrap;
        _noMapErrorLabel.Foreground = new SolidColorBrush(Color.Parse("#CCC"));

        _installPathEntry.PlaceholderText = "e.g. /path/to/Generals";
        _installPathEntry.Foreground = new SolidColorBrush(Color.Parse("#CCC"));
        _installPathEntry.Background = new SolidColorBrush(Color.Parse("#333"));
        _installPathEntry.MinWidth = 240;

        var browseButton = new Button
        {
            Content = "Browse\u2026",
            Foreground = new SolidColorBrush(Color.Parse("#CCC")),
            Background = new SolidColorBrush(Color.Parse("#444")),
        };
        browseButton.Click += OnBrowseClicked;

        var loadButton = new Button
        {
            Content = "Load",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.Parse("#336699")),
        };
        loadButton.Click += OnLoadClicked;

        var overlayStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            MaxWidth = 340,
        };
        overlayStack.Children.Add(_noMapErrorLabel);
        overlayStack.Children.Add(new TextBlock
        {
            Text = "Generals installation folder:",
            Foreground = new SolidColorBrush(Color.Parse("#888")),
        });
        overlayStack.Children.Add(_installPathEntry);
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        buttonRow.Children.Add(browseButton);
        buttonRow.Children.Add(loadButton);
        overlayStack.Children.Add(buttonRow);
        _noMapOverlay.Children.Add(overlayStack);

        var zoomButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
        };
        var zoomOut = ZoomButton("\u2212");
        zoomOut.Click += (_, _) => ZoomAtPoint(GetCanvasCenter(), -1.25f);
        var zoomIn = ZoomButton("+");
        zoomIn.Click += (_, _) => ZoomAtPoint(GetCanvasCenter(), 1.25f);
        _resetZoomBtn.Click += OnResetZoomClicked;
        _resetZoomBtn.Foreground = new SolidColorBrush(Color.Parse("#CCC"));
        _resetZoomBtn.Background = new SolidColorBrush(Color.Parse("#80333333"));
        zoomButtons.Children.Add(zoomOut);
        zoomButtons.Children.Add(zoomIn);
        zoomButtons.Children.Add(_resetZoomBtn);

        var grid = new Grid();
        grid.Children.Add(_canvas);
        grid.Children.Add(_noMapOverlay);
        grid.Children.Add(zoomButtons);
        Content = grid;
    }

    private static Button ZoomButton(string text) => new()
    {
        Content = text,
        FontSize = 14,
        Padding = new Thickness(10, 4),
        Foreground = new SolidColorBrush(Color.Parse("#CCC")),
        Background = new SolidColorBrush(Color.Parse("#80333333")),
    };

    public void BindViewModel(MainViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            _canvas.InvalidateCanvas();
            if (e.PropertyName is nameof(MainViewModel.MapLoadError) or nameof(MainViewModel.Map))
            {
                SyncNoMapOverlay();
                RebuildPreviewBitmap();
            }

            if (e.PropertyName == nameof(MainViewModel.CurrentInstallationPath))
            {
                SyncInstallationPath();
            }
        };

        SyncInstallationPath();
        SyncNoMapOverlay();
        RebuildPreviewBitmap();
    }

    private void SyncInstallationPath()
    {
        if (!string.IsNullOrEmpty(_vm?.CurrentInstallationPath))
        {
            _installPathEntry.Text = _vm.CurrentInstallationPath;
        }
    }

    private void SyncNoMapOverlay()
    {
        var hasMap = _vm?.Map != null;
        _noMapOverlay.IsVisible = !hasMap;
        if (!hasMap && _vm?.MapLoadError != null)
            _noMapErrorLabel.Text = _vm.MapLoadError;
        else if (!hasMap)
            _noMapErrorLabel.Text = "No map loaded";
    }

    private void RebuildPreviewBitmap()
    {
        var map = _vm?.Map;
        if (map == _previewBitmapSource) return;

        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewBitmapSource = map;

        if (map?.PreviewImagePixels != null && map.PreviewImageWidth > 0 && map.PreviewImageHeight > 0)
        {
            var info = new SKImageInfo(map.PreviewImageWidth, map.PreviewImageHeight,
                SKColorType.Rgba8888, SKAlphaType.Opaque);
            var bitmap = new SKBitmap(info);
            System.Runtime.InteropServices.Marshal.Copy(
                map.PreviewImagePixels, 0, bitmap.GetPixels(), map.PreviewImagePixels.Length);
            _previewBitmap = bitmap;
        }
    }

    // ── Overlay handlers ──────────────────────────────────────────────────────

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await (VisualRoot as TopLevel)!.StorageProvider
                .OpenFolderPickerAsync(new global::Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select the Generals installation folder",
                    AllowMultiple = false,
                });
            if (folders.Count > 0)
            {
                var path = folders[0].TryGetLocalPath();
                if (path != null)
                {
                    _installPathEntry.Text = path;
                    ReplaySketchLog.Write($"MapPreviewView browse selected '{path}'");
                }
            }
        }
        catch
        {
            // User cancelled or picker unavailable — no-op
            ReplaySketchLog.Write("MapPreviewView browse was cancelled or unavailable");
        }
    }

    private void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        var path = _installPathEntry.Text?.Trim();
        if (string.IsNullOrEmpty(path)) return;
        if (_vm != null)
        {
            ReplaySketchLog.Write($"MapPreviewView load clicked with path '{path}'");
            _vm.LoadMapFromFolder(path);
        }
    }

    public void SetActionTreeBridge(ActionTreeView treeView)
    {
        _treeView = treeView;
    }

    public void Invalidate() => _canvas.InvalidateCanvas();

    // ── Pointer / zoom ────────────────────────────────────────────────────────

    private void OnResetZoomClicked(object? sender, RoutedEventArgs e)
    {
        _viewMatrix = SKMatrix.Identity;
        _resetZoomBtn.IsVisible = false;
        _canvas.InvalidateCanvas();
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pointerCaptured = true;
        e.Pointer.Capture(_canvas);
        _dragStartTouch = ToCanvasPoint(e);
        _dragStartMatrix = _viewMatrix;
        _hasDragged = false;
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pointerCaptured) return;
        e.Handled = true;

        var location = ToCanvasPoint(e);
        var dx = location.X - _dragStartTouch.X;
        var dy = location.Y - _dragStartTouch.Y;
        if (!_hasDragged && MathF.Sqrt(dx * dx + dy * dy) > DragThresholdPx)
            _hasDragged = true;
        if (_hasDragged)
        {
            _viewMatrix = _dragStartMatrix.PostConcat(SKMatrix.CreateTranslation(dx, dy));
            UpdateResetButton();
            _canvas.InvalidateCanvas();
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pointerCaptured) return;
        e.Pointer.Capture(null);
        _pointerCaptured = false;
        e.Handled = true;

        if (!_hasDragged)
            HandleTap(ToCanvasPoint(e));
    }

    private void OnCanvasWheel(object? sender, PointerWheelEventArgs e)
    {
        // Wheel-zoom at cursor; Avalonia wheel deltas are small per-tick multiples.
        var location = ToCanvasPoint(e);
        var ticks = (float)e.Delta.Y * 4f;
        ZoomAtPoint(location, ticks);
        e.Handled = true;
    }

    private SKPoint ToCanvasPoint(PointerEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        return new SKPoint((float)position.X, (float)position.Y);
    }

    private void HandleTap(SKPoint canvasPos)
    {
        // Double-tap → reset zoom/pan
        var now = DateTime.UtcNow;
        if (now - _lastTapTime < DoubleTapInterval)
        {
            var ddx = canvasPos.X - _lastTapPoint.X;
            var ddy = canvasPos.Y - _lastTapPoint.Y;
            if (ddx * ddx + ddy * ddy < DoubleTapDistPx * DoubleTapDistPx)
            {
                _viewMatrix = SKMatrix.Identity;
                UpdateResetButton();
                _canvas.InvalidateCanvas();
                _lastTapTime = DateTime.MinValue;
                return;
            }
        }

        _lastTapTime = now;
        _lastTapPoint = canvasPos;

        // Map pointer position (canvas DIPs) back to pre-transform draw-call space
        if (!_viewMatrix.TryInvert(out var inverse)) return;
        var pt = inverse.MapPoint(canvasPos);

        // Hit-test existing markers
        foreach (var m in _markers)
        {
            var dx = pt.X - m.Screen.X;
            var dy = pt.Y - m.Screen.Y;
            if (dx * dx + dy * dy <= HitRadiusPx * HitRadiusPx)
            {
                _treeView?.SelectActionFromMap(m.PlayerIdx, m.ActionId);
                return;
            }
        }

        // Empty-space tap: place selected action at clicked position
        if (_vm?.SelectedActionId == null || _previewSize <= 0) return;
        var action = _vm.SelectedPlayer.Actions.FirstOrDefault(a => a.Id == _vm.SelectedActionId);
        if (action?.Position == null) return;

        var normX = Math.Clamp((pt.X - _previewOffsetX) / _previewSize, 0f, 1f);
        var normY = Math.Clamp(1f - (pt.Y - _previewOffsetY) / _previewSize, 0f, 1f);
        action.Position = new NormalizedPosition(normX, normY);
        _treeView?.RefreshSelectedNode();
        _canvas.InvalidateCanvas();
    }

    private void ZoomAtPoint(SKPoint canvasPos, float wheelTicks)
    {
        if (wheelTicks == 0) return;

        // Map wheel ticks to a smooth zoom ratio, matching the MAUI wheel handling.
        var ratio = 1f + Math.Clamp(wheelTicks / 5f, -0.3f, 0.3f);
        ZoomByRatio(canvasPos, ratio);
    }

    private void ZoomByRatio(SKPoint canvasPos, float ratio)
    {
        if (ratio <= 0f) return;

        _viewMatrix = ZoomMatrix(_viewMatrix, canvasPos, ratio);
        UpdateResetButton();
        _canvas.InvalidateCanvas();
    }

    private SKMatrix ZoomMatrix(SKMatrix baseMatrix, SKPoint canvasPos, float ratio)
    {
        var currentScale = MathF.Sqrt(baseMatrix.ScaleX * baseMatrix.ScaleX + baseMatrix.SkewY * baseMatrix.SkewY);
        var newScale = Math.Clamp(currentScale * ratio, MinScale, MaxScale);
        ratio = newScale / currentScale;

        return baseMatrix.PostConcat(SKMatrix.CreateScale(ratio, ratio, canvasPos.X, canvasPos.Y));
    }

    private SKPoint GetCanvasCenter()
    {
        var width = (float)_canvas.Bounds.Width;
        var height = (float)_canvas.Bounds.Height;
        return new SKPoint(width / 2f, height / 2f);
    }

    private void UpdateResetButton()
    {
        var isIdentity = _viewMatrix.IsIdentity;
        if (_resetZoomBtn.IsVisible == isIdentity)  // visible when NOT identity
            _resetZoomBtn.IsVisible = !isIdentity;
    }

    // ── Paint ────────────────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKCanvas canvas)
    {
        canvas.Clear(new SKColor(0x2A, 0x2A, 0x2A));

        _markers.Clear();

        var map = _vm?.Map;
        if (map == null) return;

        var width = MathF.Max((float)_canvas.Bounds.Width, 1f);
        var height = MathF.Max((float)_canvas.Bounds.Height, 1f);
        var previewSize = Math.Min(width, height);
        var offsetX = (width - previewSize) / 2f;
        var offsetY = (height - previewSize) / 2f;

        _previewOffsetX = offsetX;
        _previewOffsetY = offsetY;
        _previewSize = previewSize;

        canvas.Save();
        canvas.Concat(in _viewMatrix);

        var markers = MapPreviewRenderer.Render(
            canvas, map, _previewBitmap,
            _vm!.Scenario.Players,
            _vm.Scenario.BaseRadiusWorldUnits,
            _vm.SelectedSlotIndex,
            _vm.SelectedActionId,
            offsetX, offsetY, previewSize);

        canvas.Restore();

        foreach (var m in markers)
            _markers.Add(new MarkerInfo(m.PlayerIdx, m.ActionId, new SKPoint(m.ScreenX, m.ScreenY)));
    }
}
