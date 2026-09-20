using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using global::Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OpenSage.Tools.ReplaySketch.Avalonia.Controls;
using OpenSage.Tools.ReplaySketch.Avalonia.ViewModels;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace OpenSage.Tools.ReplaySketch.Avalonia.Views;

/// <summary>
/// Validation Ground editor tab: zoomable map canvas with Move/Pan tools and layout knobs,
/// painted through the Core ValidationGroundMapRenderer. Ported from the MAUI view; pointer
/// events and the Avalonia StorageProvider replace SKTouch and MAUI pickers.
/// </summary>
public sealed class ValidationGroundEditorView : UserControl
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
    private bool _pointerCaptured;
    private SKPoint _dragStartTouch;
    private SKMatrix _dragStartMatrix;
    private bool _hasDragged;

    // Move-tool drag target (null when not dragging a placement).
    private (int FactionIndex, int RosterIndex, bool IsTarget)? _dragPlacement;

    private readonly SkiaCanvas _canvas = new();
    private readonly Button _moveToolBtn = new() { Content = "Move" };
    private readonly Button _panToolBtn = new() { Content = "Pan" };
    private readonly TextBlock _hintLabel = new();
    private readonly TextBox _clusterXEntry = new();
    private readonly TextBox _clusterYEntry = new();
    private readonly TextBox _unitSpacingEntry = new();
    private readonly TextBox _factionRowOffsetEntry = new();
    private readonly TextBox _targetApproachEntry = new();
    private readonly TextBlock _statusLabel = new();

    public ValidationGroundEditorView()
    {
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.PointerPressed += OnCanvasPointerPressed;
        _canvas.PointerMoved += OnCanvasPointerMoved;
        _canvas.PointerReleased += OnCanvasPointerReleased;
        _canvas.PointerWheelChanged += OnCanvasWheel;

        var canvasArea = new Grid();
        canvasArea.Children.Add(_canvas);

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
        };
        _moveToolBtn.Click += OnMoveToolClicked;
        _panToolBtn.Click += OnPanToolClicked;
        _hintLabel.VerticalAlignment = VerticalAlignment.Center;
        _hintLabel.Foreground = new SolidColorBrush(Color.Parse("#AAA"));
        tools.Children.Add(_moveToolBtn);
        tools.Children.Add(_panToolBtn);
        tools.Children.Add(_hintLabel);
        canvasArea.Children.Add(tools);

        var zoomButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
        };
        var zoomOut = ZoomButton("\u2212");
        zoomOut.Click += (_, _) => ZoomAtPoint(GetCanvasCenter(), -600f);
        var zoomIn = ZoomButton("+");
        zoomIn.Click += (_, _) => ZoomAtPoint(GetCanvasCenter(), 600f);
        var reset = ZoomButton("\u21BA Reset");
        reset.Click += OnResetZoomClicked;
        zoomButtons.Children.Add(zoomOut);
        zoomButtons.Children.Add(zoomIn);
        zoomButtons.Children.Add(reset);
        canvasArea.Children.Add(zoomButtons);

        var knobs = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
        knobs.Children.Add(KnobRow("Cluster X", _clusterXEntry));
        knobs.Children.Add(KnobRow("Cluster Y", _clusterYEntry));
        knobs.Children.Add(KnobRow("Unit spacing", _unitSpacingEntry));
        knobs.Children.Add(KnobRow("Faction row offset", _factionRowOffsetEntry));
        knobs.Children.Add(KnobRow("Target approach", _targetApproachEntry));

        var regenerate = new Button
        {
            Content = "Regenerate",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.Parse("#336699")),
        };
        regenerate.Click += OnRegenerateClicked;
        knobs.Children.Add(regenerate);

        var load = new Button { Content = "Load\u2026" };
        load.Click += OnLoadClicked;
        knobs.Children.Add(load);

        var save = new Button { Content = "Save\u2026" };
        save.Click += OnSaveClicked;
        knobs.Children.Add(save);

        _statusLabel.TextWrapping = TextWrapping.Wrap;
        knobs.Children.Add(_statusLabel);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,230") };
        grid.Children.Add(canvasArea);
        Grid.SetColumn(knobs, 1);
        grid.Children.Add(knobs);

        Content = grid;
    }

    private static Button ZoomButton(string text) => new()
    {
        Content = text,
        Foreground = new SolidColorBrush(Color.Parse("#CCC")),
        Background = new SolidColorBrush(Color.Parse("#80333333")),
    };

    private static Control KnobRow(string label, TextBox entry)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70") };
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(entry, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(entry);
        return grid;
    }

    public void Invalidate() => _canvas.InvalidateCanvas();

    public void BindViewModel(MainViewModel vm)
    {
        _vm = vm;

        _model = new ValidationGroundEditorModel();
        _model.LoadScenario(ValidationGroundScenarioStore.Standard());
        _model.PropertyChanged += (_, _) => _canvas.InvalidateCanvas();
        PopulateKnobs();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Map) or nameof(MainViewModel.MapLoadError))
            {
                AttachVmMap();
                RebuildPreviewBitmap();
            }
            _canvas.InvalidateCanvas();
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

    private void OnPaintSurface(object? sender, SKCanvas canvas)
    {
        canvas.Clear(new SKColor(0x2A, 0x2A, 0x2A));

        var map = _model.Map;
        var plan = _model.Plan;
        if (map == null || plan == null) return;

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
        ValidationGroundMapRenderer.Render(canvas, map, _previewBitmap, plan, _model.Selected, offsetX, offsetY, previewSize);
        canvas.Restore();
    }

    // ── Pointer ────────────────────────────────────────────────────────────────

    private SKPoint ToCanvasPoint(PointerEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        return new SKPoint((float)position.X, (float)position.Y);
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pointerCaptured = true;
        e.Pointer.Capture(_canvas);
        _dragStartTouch = ToCanvasPoint(e);
        _dragStartMatrix = _viewMatrix;
        _hasDragged = false;
        if (_model.Tool == EditorTool.Move)
        {
            BeginMoveDrag(_dragStartTouch);
        }
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pointerCaptured) return;
        e.Handled = true;

        var location = ToCanvasPoint(e);

        if (_model.Tool == EditorTool.Move && _dragPlacement.HasValue)
        {
            ContinueMoveDrag(location);
            _hasDragged = true;
        }
        else
        {
            // Pan tool, or Move tool with no grabbed marker.
            var dx = location.X - _dragStartTouch.X;
            var dy = location.Y - _dragStartTouch.Y;
            if (!_hasDragged && MathF.Sqrt(dx * dx + dy * dy) > 6f)
                _hasDragged = true;
            if (_hasDragged)
            {
                _viewMatrix = _dragStartMatrix.PostConcat(SKMatrix.CreateTranslation(dx, dy));
                _canvas.InvalidateCanvas();
            }
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pointerCaptured) return;
        e.Pointer.Capture(null);
        _pointerCaptured = false;
        _dragPlacement = null;
        e.Handled = true;
    }

    private void OnCanvasWheel(object? sender, PointerWheelEventArgs e)
    {
        // Match the MAUI wheel mapping (~120 units per tick scaled up).
        ZoomAtPoint(ToCanvasPoint(e), (float)e.Delta.Y * 120f);
        e.Handled = true;
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
            _canvas.InvalidateCanvas();
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
        _canvas.InvalidateCanvas();
    }

    // ── Toolbar ──────────────────────────────────────────────────────────────

    private void OnMoveToolClicked(object? sender, RoutedEventArgs e)
    {
        _model.Tool = EditorTool.Move;
        SyncToolButtons();
    }

    private void OnPanToolClicked(object? sender, RoutedEventArgs e)
    {
        _model.Tool = EditorTool.Pan;
        SyncToolButtons();
    }

    private void SyncToolButtons()
    {
        var moveActive = _model.Tool == EditorTool.Move;
        _moveToolBtn.Background = new SolidColorBrush(moveActive ? Color.Parse("#336699") : Color.Parse("#80333333"));
        _moveToolBtn.Foreground = moveActive ? Brushes.White : new SolidColorBrush(Color.Parse("#CCCCCC"));
        _panToolBtn.Background = new SolidColorBrush(!moveActive ? Color.Parse("#336699") : Color.Parse("#80333333"));
        _panToolBtn.Foreground = !moveActive ? Brushes.White : new SolidColorBrush(Color.Parse("#CCCCCC"));
        _hintLabel.Text = moveActive
            ? "Move: drag a marker to reposition"
            : "Pan: drag to scroll, wheel to zoom";
    }

    private void OnResetZoomClicked(object? sender, RoutedEventArgs e)
    {
        _viewMatrix = SKMatrix.Identity;
        _canvas.InvalidateCanvas();
    }

    private SKPoint GetCanvasCenter() => new(
        MathF.Max((float)_canvas.Bounds.Width, 1f) / 2f,
        MathF.Max((float)_canvas.Bounds.Height, 1f) / 2f);

    // ── Knobs ────────────────────────────────────────────────────────────────

    private void PopulateKnobs()
    {
        var l = _model.Scenario.Layout;
        _clusterXEntry.Text = l.TargetClusterCenter.X.ToString("0.#", CultureInfo.InvariantCulture);
        _clusterYEntry.Text = l.TargetClusterCenter.Y.ToString("0.#", CultureInfo.InvariantCulture);
        _unitSpacingEntry.Text = l.UnitSpacing.ToString("0.#", CultureInfo.InvariantCulture);
        _factionRowOffsetEntry.Text = l.FactionRowOffset.ToString("0.#", CultureInfo.InvariantCulture);
        _targetApproachEntry.Text = l.TargetApproachDistance.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private bool TryReadFloat(TextBox entry, out float value) =>
        float.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void OnRegenerateClicked(object? sender, RoutedEventArgs e)
    {
        var l = _model.Scenario.Layout;
        if (TryReadFloat(_clusterXEntry, out var cx) && TryReadFloat(_clusterYEntry, out var cy))
            l.TargetClusterCenter = new System.Numerics.Vector3(cx, cy, 0f);
        if (TryReadFloat(_unitSpacingEntry, out var us)) l.UnitSpacing = us;
        if (TryReadFloat(_factionRowOffsetEntry, out var fro)) l.FactionRowOffset = fro;
        if (TryReadFloat(_targetApproachEntry, out var ta)) l.TargetApproachDistance = ta;

        _model.Regenerate();
        PopulateKnobs();
        SetStatus("Regenerated from knobs.");
    }

    // ── Save / Load ──────────────────────────────────────────────────────────

    private async void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = (VisualRoot as TopLevel)!;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Load validation-ground scenario",
                AllowMultiple = false,
            });
            if (files.Count == 0) return;

            var path = files[0].TryGetLocalPath();
            if (path == null) return;

            var json = await File.ReadAllTextAsync(path);
            _model.LoadScenarioJson(json);
            PopulateKnobs();
            SetStatus($"Loaded {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Load failed: {ex.Message}");
        }
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = (VisualRoot as TopLevel)!;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save validation-ground scenario",
                SuggestedFileName = "validation-ground.json",
            });
            if (file == null) return;

            var path = file.TryGetLocalPath();
            if (path == null) return;

            await File.WriteAllTextAsync(path, _model.GetScenarioJson());
            SetStatus($"Saved {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    private void SetStatus(string text) => _statusLabel.Text = text;
}
