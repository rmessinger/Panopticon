using CommunityToolkit.Maui.Storage;
using OpenSage.Tools.ReplaySketch.Maui.ViewModels;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace OpenSage.Tools.ReplaySketch.Maui.Views;

public partial class MapPreviewView : ContentView
{
    private MainViewModel? _vm;
    private ActionTreeView? _treeView;

    // Cached bird's-eye terrain thumbnail for the current map
    private SKBitmap? _previewBitmap;
    private MapMetadataService? _previewBitmapSource;

    // Cached base layout — updated every OnPaintSurface call
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
    private long _dragTouchId = -1;
    private SKPoint _dragStartTouch;
    private SKMatrix _dragStartMatrix;
    private bool _hasDragged;
    private const float DragThresholdPx = 6f;

    // Double-tap reset detection
    private DateTime _lastTapTime = DateTime.MinValue;
    private SKPoint _lastTapPoint;
    private static readonly TimeSpan DoubleTapInterval = TimeSpan.FromMilliseconds(350);
    private const float DoubleTapDistPx = 30f;

    // Trackpad pinch state
    private SKMatrix _pinchStartMatrix = SKMatrix.Identity;

    public MapPreviewView()
    {
        InitializeComponent();
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnCanvasPinchUpdated;
        Canvas.GestureRecognizers.Add(pinch);
    }

    public void BindViewModel(MainViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            Canvas.InvalidateSurface();
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
            InstallPathEntry.Text = _vm.CurrentInstallationPath;
        }
    }

    private void SyncNoMapOverlay()
    {
        var hasMap = _vm?.Map != null;
        NoMapOverlay.IsVisible = !hasMap;
        if (!hasMap && _vm?.MapLoadError != null)
            NoMapErrorLabel.Text = _vm.MapLoadError;
        else if (!hasMap)
            NoMapErrorLabel.Text = "No map loaded";
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

    // ── No-map overlay handlers ────────────────────────────────────────────────

    private async void OnBrowseClicked(object? sender, EventArgs e)
    {
        // Use a folder picker so the user can point directly at the installation root.
        try
        {
            var result = await FolderPicker.Default.PickAsync(default);
            if (result.IsSuccessful)
            {
                InstallPathEntry.Text = result.Folder.Path;
                ReplaySketchLog.Write($"MapPreviewView browse selected '{result.Folder.Path}'");
            }
        }
        catch
        {
            // User cancelled or picker unavailable — no-op
            ReplaySketchLog.Write("MapPreviewView browse was cancelled or unavailable");
        }
    }

    private void OnLoadClicked(object? sender, EventArgs e)
    {
        var path = InstallPathEntry.Text?.Trim();
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

    public void Invalidate() => Canvas.InvalidateSurface();

    private float GetDensity() =>
        Canvas.CanvasSize.Width > 0 && Canvas.Width > 0
            ? (float)(Canvas.CanvasSize.Width / Canvas.Width)
            : 1f;

    // ── Touch / zoom ─────────────────────────────────────────────────────────

    private void OnResetZoomClicked(object? sender, EventArgs e)
    {
        _viewMatrix = SKMatrix.Identity;
        ResetZoomBtn.IsVisible = false;
        Canvas.InvalidateSurface();
    }

    private void OnZoomInClicked(object? sender, EventArgs e)
    {
        ZoomAtPoint(GetCanvasCenter(), 1.25f);
    }

    private void OnZoomOutClicked(object? sender, EventArgs e)
    {
        ZoomAtPoint(GetCanvasCenter(), 0.8f);
    }

    private void OnCanvasPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchStartMatrix = _viewMatrix;
                break;

            case GestureStatus.Running:
                ApplyPinchZoom(e.ScaleOrigin, (float)e.Scale);
                break;

            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                break;
        }
    }

    private void OnCanvasTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _dragTouchId = e.Id;
                _dragStartTouch = e.Location;
                _dragStartMatrix = _viewMatrix;
                _hasDragged = false;
                break;

            case SKTouchAction.Moved:
                if (e.Id == _dragTouchId)
                {
                    var dx = e.Location.X - _dragStartTouch.X;
                    var dy = e.Location.Y - _dragStartTouch.Y;
                    if (!_hasDragged && MathF.Sqrt(dx * dx + dy * dy) > DragThresholdPx)
                        _hasDragged = true;
                    if (_hasDragged)
                    {
                        _viewMatrix = _dragStartMatrix.PostConcat(SKMatrix.CreateTranslation(dx, dy));
                        UpdateResetButton();
                        Canvas.InvalidateSurface();
                    }
                }
                break;

            case SKTouchAction.Released:
                if (e.Id == _dragTouchId && !_hasDragged)
                    HandleTap(e.Location);
                _dragTouchId = -1;
                break;

            case SKTouchAction.WheelChanged:
                // Scroll wheel / trackpad scroll → zoom at cursor
                ZoomAtPoint(e.Location, e.WheelDelta);
                break;
        }
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
                Canvas.InvalidateSurface();
                _lastTapTime = DateTime.MinValue;
                return;
            }
        }
        _lastTapTime = now;
        _lastTapPoint = canvasPos;

        // Map touch position (canvas pixels) back to pre-transform draw-call space
        if (!_viewMatrix.TryInvert(out var inverse)) return;
        var pt = inverse.MapPoint(canvasPos);

        var density = GetDensity();
        var hitR = HitRadiusPx * density;

        // Hit-test existing markers
        foreach (var m in _markers)
        {
            var dx = pt.X - m.Screen.X;
            var dy = pt.Y - m.Screen.Y;
            if (dx * dx + dy * dy <= hitR * hitR)
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
        Canvas.InvalidateSurface();
    }

    private void ZoomAtPoint(SKPoint canvasPos, float wheelDelta)
    {
        if (wheelDelta == 0) return;

        // Each wheel tick is ~120 units; map to a smooth zoom ratio
        var ratio = 1f + Math.Clamp(wheelDelta / 600f, -0.3f, 0.3f);
        ZoomByRatio(canvasPos, ratio);
    }

    private void ZoomByRatio(SKPoint canvasPos, float ratio)
    {
        if (ratio <= 0f) return;

        _viewMatrix = ZoomMatrix(_viewMatrix, canvasPos, ratio);
        UpdateResetButton();
        Canvas.InvalidateSurface();
    }

    private void ApplyPinchZoom(Point scaleOrigin, float scale)
    {
        if (scale <= 0f) return;

        var origin = GetCanvasPoint(scaleOrigin);
        _viewMatrix = ZoomMatrix(_pinchStartMatrix, origin, scale);
        UpdateResetButton();
        Canvas.InvalidateSurface();
    }

    private SKMatrix ZoomMatrix(SKMatrix baseMatrix, SKPoint canvasPos, float ratio)
    {
        // Extract current uniform scale from matrix and clamp
        var currentScale = MathF.Sqrt(baseMatrix.ScaleX * baseMatrix.ScaleX + baseMatrix.SkewY * baseMatrix.SkewY);
        var newScale = Math.Clamp(currentScale * ratio, MinScale, MaxScale);
        ratio = newScale / currentScale;

        return baseMatrix.PostConcat(SKMatrix.CreateScale(ratio, ratio, canvasPos.X, canvasPos.Y));
    }

    private SKPoint GetCanvasCenter()
    {
        var width = (float)Canvas.CanvasSize.Width;
        var height = (float)Canvas.CanvasSize.Height;
        if (width <= 0f || height <= 0f)
        {
            width = (float)Canvas.Width;
            height = (float)Canvas.Height;
        }

        return new SKPoint(width / 2f, height / 2f);
    }

    private SKPoint GetCanvasPoint(Point origin)
    {
        var width = (float)Canvas.CanvasSize.Width;
        var height = (float)Canvas.CanvasSize.Height;
        if (width <= 0f || height <= 0f)
        {
            width = (float)Canvas.Width;
            height = (float)Canvas.Height;
        }

        var x = origin.X <= 1f ? origin.X * width : origin.X;
        var y = origin.Y <= 1f ? origin.Y * height : origin.Y;
        return new SKPoint((float)x, (float)y);
    }

    private void UpdateResetButton()
    {
        var isIdentity = _viewMatrix.IsIdentity;
        if (ResetZoomBtn.IsVisible == isIdentity)  // visible when NOT identity
            ResetZoomBtn.IsVisible = !isIdentity;
    }

    // ── Paint ────────────────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x2A, 0x2A, 0x2A));

        _markers.Clear();

        var map = _vm?.Map;
        if (map == null) return;

        var info = e.Info;
        var previewSize = Math.Min(info.Width, info.Height);
        var offsetX = (info.Width - previewSize) / 2f;
        var offsetY = (info.Height - previewSize) / 2f;

        // Cache base layout for touch handler
        _previewOffsetX = offsetX;
        _previewOffsetY = offsetY;
        _previewSize = previewSize;

        // Apply zoom/pan transform — _viewMatrix operates in physical canvas pixel space
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
