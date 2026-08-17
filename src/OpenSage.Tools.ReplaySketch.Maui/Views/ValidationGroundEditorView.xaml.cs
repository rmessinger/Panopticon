using System.Globalization;
using System.Text;
using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Storage;
using OpenSage.Tools.ReplaySketch.Maui.ViewModels;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace OpenSage.Tools.ReplaySketch.Maui.Views;

public partial class ValidationGroundEditorView : ContentView
{
    private MainViewModel? _vm;
    private ValidationGroundEditorModel _model = new();

    private SKBitmap? _previewBitmap;
    private MapMetadataService? _previewBitmapSource;

    // Cached base layout for hit-testing / unprojection (pre-view-transform canvas px).
    private float _previewOffsetX;
    private float _previewOffsetY;
    private float _previewSize;

    // ── Zoom / pan ────────────────────────────────────────────────────────────
    private SKMatrix _viewMatrix = SKMatrix.Identity;
    private const float MinScale = 0.5f;
    private const float MaxScale = 10f;
    private const float HitRadiusPx = 14f;

    // Single-pointer drag state (used by both Pan and Move tools).
    private long _dragTouchId = -1;
    private SKPoint _dragStartTouch;
    private SKMatrix _dragStartMatrix;
    private bool _hasDragged;

    // Move-tool drag target (null when not dragging a placement).
    private (int FactionIndex, int RosterIndex, bool IsTarget)? _dragPlacement;

    public ValidationGroundEditorView()
    {
        InitializeComponent();
    }

    public void Invalidate() => Canvas.InvalidateSurface();

    public void BindViewModel(MainViewModel vm)
    {
        _vm = vm;

        _model = new ValidationGroundEditorModel();
        _model.LoadScenario(ValidationGroundScenarioStore.Standard());
        _model.PropertyChanged += (_, _) => Canvas.InvalidateSurface();
        PopulateKnobs();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Map) or nameof(MainViewModel.MapLoadError))
            {
                AttachVmMap();
                RebuildPreviewBitmap();
            }
            Canvas.InvalidateSurface();
        };
        AttachVmMap();
        RebuildPreviewBitmap();
        SyncToolButtons();
    }

    private void AttachVmMap()
    {
        if (_vm?.Map != null)
        {
            _model.AttachMap(_vm.Map);
        }
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

    // ── Paint ────────────────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x2A, 0x2A, 0x2A));

        var map = _model.Map;
        var plan = _model.Plan;
        if (map == null || plan == null) return;

        var info = e.Info;
        var previewSize = Math.Min(info.Width, info.Height);
        var offsetX = (info.Width - previewSize) / 2f;
        var offsetY = (info.Height - previewSize) / 2f;

        _previewOffsetX = offsetX;
        _previewOffsetY = offsetY;
        _previewSize = previewSize;

        canvas.Save();
        canvas.Concat(in _viewMatrix);
        ValidationGroundMapRenderer.Render(canvas, map, _previewBitmap, plan, _model.Selected, offsetX, offsetY, previewSize);
        canvas.Restore();
    }

    // ── Touch ────────────────────────────────────────────────────────────────

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
                if (_model.Tool == EditorTool.Move)
                {
                    BeginMoveDrag(e.Location);
                }
                break;

            case SKTouchAction.Moved:
                if (e.Id != _dragTouchId) break;

                if (_model.Tool == EditorTool.Move && _dragPlacement.HasValue)
                {
                    ContinueMoveDrag(e.Location);
                    _hasDragged = true;
                }
                else
                {
                    // Pan tool, or Move tool with no grabbed marker.
                    var dx = e.Location.X - _dragStartTouch.X;
                    var dy = e.Location.Y - _dragStartTouch.Y;
                    if (!_hasDragged && MathF.Sqrt(dx * dx + dy * dy) > 6f)
                        _hasDragged = true;
                    if (_hasDragged)
                    {
                        _viewMatrix = _dragStartMatrix.PostConcat(SKMatrix.CreateTranslation(dx, dy));
                        Canvas.InvalidateSurface();
                    }
                }
                break;

            case SKTouchAction.Released:
                _dragPlacement = null;
                _dragTouchId = -1;
                break;

            case SKTouchAction.WheelChanged:
                ZoomAtPoint(e.Location, e.WheelDelta);
                break;
        }
    }

    private void BeginMoveDrag(SKPoint canvasPos)
    {
        if (_model.Map == null || _previewSize <= 0) return;
        if (!_viewMatrix.TryInvert(out var inverse)) return;
        var pt = inverse.MapPoint(canvasPos);

        var hit = _model.HitTest(pt.X, pt.Y, _previewOffsetX, _previewOffsetY, _previewSize, HitRadiusPx);
        if (hit.HasValue)
        {
            _dragPlacement = (hit.Value.FactionIndex, hit.Value.RosterIndex, hit.Value.IsTarget);
            _model.Selected = _dragPlacement;
            Canvas.InvalidateSurface();
        }
        else
        {
            _model.Selected = null;
        }
    }

    private void ContinueMoveDrag(SKPoint canvasPos)
    {
        if (!_dragPlacement.HasValue || _model.Map == null || _previewSize <= 0) return;
        if (!_viewMatrix.TryInvert(out var inverse)) return;
        var pt = inverse.MapPoint(canvasPos);

        var extentMin = _model.Map.ExtentMin;
        var extentMax = _model.Map.ExtentMax;
        var world = ValidationGroundEditorModel.Unproject(extentMin, extentMax, pt.X, pt.Y, _previewOffsetX, _previewOffsetY, _previewSize);

        var d = _dragPlacement.Value;
        _model.MovePlacement(d.FactionIndex, d.RosterIndex, d.IsTarget, world.X, world.Y);
    }

    private void ZoomAtPoint(SKPoint canvasPos, float wheelDelta)
    {
        if (wheelDelta == 0) return;
        var ratio = 1f + Math.Clamp(wheelDelta / 600f, -0.3f, 0.3f);
        if (ratio <= 0f) return;

        var currentScale = MathF.Sqrt(_viewMatrix.ScaleX * _viewMatrix.ScaleX + _viewMatrix.SkewY * _viewMatrix.SkewY);
        var newScale = Math.Clamp(currentScale * ratio, MinScale, MaxScale);
        ratio = newScale / currentScale;
        _viewMatrix = _viewMatrix.PostConcat(SKMatrix.CreateScale(ratio, ratio, canvasPos.X, canvasPos.Y));
        Canvas.InvalidateSurface();
    }

    // ── Toolbar ──────────────────────────────────────────────────────────────

    private void OnMoveToolClicked(object? sender, EventArgs e)
    {
        _model.Tool = EditorTool.Move;
        SyncToolButtons();
    }

    private void OnPanToolClicked(object? sender, EventArgs e)
    {
        _model.Tool = EditorTool.Pan;
        SyncToolButtons();
    }

    private void SyncToolButtons()
    {
        var moveActive = _model.Tool == EditorTool.Move;
        MoveToolBtn.BackgroundColor = moveActive ? Color.FromArgb("#336699") : Color.FromArgb("#80333333");
        MoveToolBtn.TextColor = moveActive ? Colors.White : Color.FromArgb("#CCCCCC");
        PanToolBtn.BackgroundColor = !moveActive ? Color.FromArgb("#336699") : Color.FromArgb("#80333333");
        PanToolBtn.TextColor = !moveActive ? Colors.White : Color.FromArgb("#CCCCCC");
        HintLabel.Text = moveActive
            ? "Move: drag a marker to reposition"
            : "Pan: drag to scroll, wheel to zoom";
    }

    private void OnZoomInClicked(object? sender, EventArgs e) =>
        ZoomAtPoint(GetCanvasCenter(), 600f);

    private void OnZoomOutClicked(object? sender, EventArgs e) =>
        ZoomAtPoint(GetCanvasCenter(), -600f);

    private void OnResetZoomClicked(object? sender, EventArgs e)
    {
        _viewMatrix = SKMatrix.Identity;
        Canvas.InvalidateSurface();
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

    // ── Knobs ────────────────────────────────────────────────────────────────

    private void PopulateKnobs()
    {
        var l = _model.Scenario.Layout;
        ClusterXEntry.Text = l.TargetClusterCenter.X.ToString("0.#", CultureInfo.InvariantCulture);
        ClusterYEntry.Text = l.TargetClusterCenter.Y.ToString("0.#", CultureInfo.InvariantCulture);
        UnitSpacingEntry.Text = l.UnitSpacing.ToString("0.#", CultureInfo.InvariantCulture);
        FactionRowOffsetEntry.Text = l.FactionRowOffset.ToString("0.#", CultureInfo.InvariantCulture);
        TargetApproachEntry.Text = l.TargetApproachDistance.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private bool TryReadFloat(Entry entry, out float value) =>
        float.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void OnRegenerateClicked(object? sender, EventArgs e)
    {
        var l = _model.Scenario.Layout;
        if (TryReadFloat(ClusterXEntry, out var cx) && TryReadFloat(ClusterYEntry, out var cy))
            l.TargetClusterCenter = new System.Numerics.Vector3(cx, cy, 0f);
        if (TryReadFloat(UnitSpacingEntry, out var us)) l.UnitSpacing = us;
        if (TryReadFloat(FactionRowOffsetEntry, out var fro)) l.FactionRowOffset = fro;
        if (TryReadFloat(TargetApproachEntry, out var ta)) l.TargetApproachDistance = ta;

        _model.Regenerate();
        PopulateKnobs();
        SetStatus("Regenerated from knobs.");
    }

    // ── Save / Load ──────────────────────────────────────────────────────────

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Load validation-ground scenario",
            });
            if (result == null) return;

            var json = await File.ReadAllTextAsync(result.FullPath);
            _model.LoadScenarioJson(json);
            PopulateKnobs();
            SetStatus($"Loaded {result.FileName}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Load failed: {ex.Message}");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        // FileSaver needs maccatalyst 14+ or Windows; guard so the Linux library build and earlier
        // macOS runtimes do not warn (CA1416) or call an unsupported API.
        if (!OperatingSystem.IsMacCatalystVersionAtLeast(14) && !OperatingSystem.IsWindows())
        {
            SetStatus("Save is not supported on this platform.");
            return;
        }

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(_model.GetScenarioJson()));
            var path = await FileSaver.Default.SaveAsync("validation-ground.json", stream);
            if (path == null) return;
            SetStatus($"Saved {Path.GetFileName(path.FilePath)}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    private void SetStatus(string text) => StatusLabel.Text = text;
}
