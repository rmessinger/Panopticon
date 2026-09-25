using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Controls;

/// <summary>
/// Paints through SkiaSharp by leasing Avalonia's own Skia surface, so the Core renderers
/// (MapPreviewRenderer, ActionTreeRenderer, ValidationGroundMapRenderer) draw unchanged.
/// Rendering happens in DIP space (density 1). Set <see cref="DesiredCanvasSize"/> when the
/// painted content needs to drive layout (e.g. the action tree inside a scroll viewer).
/// </summary>
public sealed class SkiaCanvas : Control
{
    public static readonly StyledProperty<Size> DesiredCanvasSizeProperty =
        AvaloniaProperty.Register<SkiaCanvas, Size>(nameof(DesiredCanvasSize), new Size(0, 0));

    /// <summary>Raised when the control paints; draw through the leased SKCanvas.</summary>
    public event EventHandler<SKCanvas>? PaintSurface;

    public Size DesiredCanvasSize
    {
        get => GetValue(DesiredCanvasSizeProperty);
        set => SetValue(DesiredCanvasSizeProperty, value);
    }

    static SkiaCanvas()
    {
        AffectsMeasure<SkiaCanvas>(DesiredCanvasSizeProperty);
        AffectsRender<SkiaCanvas>(DesiredCanvasSizeProperty);
    }

    public void InvalidateCanvas() => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var handlers = PaintSurface;
        if (handlers is null)
        {
            return;
        }

        context.Custom(new SkiaDrawOperation(canvas => handlers(this, canvas), new Rect(Bounds.Size)));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = DesiredCanvasSize;
        if (desired.Width <= 0 || desired.Height <= 0)
        {
            return availableSize;
        }

        return new Size(
            Math.Min(desired.Width, availableSize.Width),
            Math.Min(desired.Height, availableSize.Height));
    }

    /// <summary>
    /// Bridges Avalonia's render pipeline to raw SkiaSharp drawing through the platform
    /// lease feature. The leased canvas carries Avalonia's current drawing state; Core
    /// renderers draw in DIP space.
    /// </summary>
    private sealed class SkiaDrawOperation(Action<SKCanvas> draw, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public void Dispose()
        {
        }

        public bool HitTest(Point point) => false;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            draw(lease.SkCanvas);
        }

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
    }
}
