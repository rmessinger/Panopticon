using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Panopticon.Graphics;
using Panopticon.Tools.WorldBuilder.Core;
using Veldrid;
using AvPixelFormat = Avalonia.Platform.PixelFormat;
using AvVector = Avalonia.Vector;

namespace Panopticon.Tools.WorldBuilder.Avalonia.Controls;

/// <summary>
/// Hosts the WorldBuilder 3D viewport in Avalonia. The game renders into its
/// offscreen panel framebuffer; this control reads each frame back and
/// presents it through a writeable bitmap, driven by
/// <see cref="TopLevel.RequestAnimationFrame(Action{TimeSpan})"/>. Pointer and
/// key events are translated into the platform-neutral Core event types.
/// </summary>
public sealed class WorldBuilderViewport : Control
{
    private GameViewportRenderer? _renderer;
    private WriteableBitmap? _bitmap;
    private long _presentedVersion = -1;
    private bool _running;

    /// <summary>Raised for every key event, after camera input dispatch.</summary>
    public event Action<ViewportKeyEvent>? KeyEventReceived;

    public GameViewportRenderer? Renderer
    {
        get => _renderer;
        set
        {
            _renderer = value;
            _presentedVersion = -1;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _running = true;
        LoopFrame(TimeSpan.Zero);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _running = false;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void LoopFrame(TimeSpan time)
    {
        if (!_running)
        {
            return;
        }

        try
        {
            Tick();
        }
        finally
        {
            if (VisualRoot is TopLevel topLevel)
            {
                topLevel.RequestAnimationFrame(LoopFrame);
            }
        }
    }

    private void Tick()
    {
        var renderer = _renderer;
        if (renderer is null)
        {
            return;
        }

        SyncSize(renderer);

        if (!renderer.IsInitialized)
        {
            return;
        }

        renderer.RenderFrame();

        if (renderer.FrameTexture is { } frame &&
            renderer.GraphicsDevice is { } device &&
            _presentedVersion != renderer.FrameVersion)
        {
            _presentedVersion = renderer.FrameVersion;
            PresentFrame(device, frame);
        }
    }

    private void SyncSize(GameViewportRenderer renderer)
    {
        var scale = VisualRoot is TopLevel topLevel ? topLevel.RenderScaling : 1.0;
        var width = (uint)Math.Floor(Bounds.Width * scale);
        var height = (uint)Math.Floor(Bounds.Height * scale);
        if (width == 0 || height == 0)
        {
            return;
        }

        if (!renderer.IsInitialized)
        {
            renderer.Initialize(width, height);
        }
        else if (renderer.Width != width || renderer.Height != height)
        {
            renderer.Resize(width, height);
        }
    }

    private unsafe void PresentFrame(GraphicsDevice device, Texture frame)
    {
        var captured = FrameCapturer.Capture(device, frame);
        var width = captured.Width;
        var height = captured.Height;

        if (_bitmap is null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new AvVector(96, 96),
                AvPixelFormat.Bgra8888);
        }

        using (var lockBuffer = _bitmap.Lock())
        {
            var source = captured.PixelData;
            var stride = width * 4;
            var isBgra = captured.Format is Veldrid.PixelFormat.B8_G8_R8_A8_UNorm
                                       or Veldrid.PixelFormat.B8_G8_R8_A8_UNorm_SRgb;

            fixed (byte* src = source)
            {
                for (var y = 0; y < height; y++)
                {
                    var row = src + y * stride;
                    var destination = (byte*)lockBuffer.Address + y * lockBuffer.RowBytes;
                    for (var x = 0; x < stride; x += 4)
                    {
                        if (isBgra)
                        {
                            destination[x] = row[x];
                            destination[x + 1] = row[x + 1];
                            destination[x + 2] = row[x + 2];
                            destination[x + 3] = row[x + 3];
                        }
                        else
                        {
                            destination[x] = row[x + 2];
                            destination[x + 1] = row[x + 1];
                            destination[x + 2] = row[x];
                            destination[x + 3] = row[x + 3];
                        }
                    }
                }
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_bitmap is { } bitmap)
        {
            context.DrawImage(
                bitmap,
                new Rect(0, 0, Bounds.Width, Bounds.Height),
                new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height));
        }
        else
        {
            context.FillRectangle(
                new SolidColorBrush(WbTheme.ViewportBack),
                new Rect(Bounds.Size));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ForwardPointer(e, ViewportPointerEventType.Down, e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ForwardPointer(e, ViewportPointerEventType.Move, e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ForwardPointer(e, ViewportPointerEventType.Up, e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_renderer is null)
        {
            return;
        }

        _renderer.OnPointerEvent(new ViewportPointerEvent
        {
            Position = ToViewport(e),
            EventType = ViewportPointerEventType.Scroll,
            Button = ViewportMouseButton.None,
            ScrollDelta = (float)e.Delta.Y,
        });
        e.Handled = true;
    }

    private void ForwardPointer(PointerEventArgs e, ViewportPointerEventType type, PointerUpdateKind updateKind)
    {
        if (_renderer is null)
        {
            return;
        }

        _renderer.OnPointerEvent(new ViewportPointerEvent
        {
            Position = ToViewport(e),
            EventType = type,
            Button = MapButton(updateKind),
        });
    }

    private Vector2 ToViewport(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var scale = VisualRoot is TopLevel topLevel ? topLevel.RenderScaling : 1.0;
        return new Vector2((float)(point.X * scale), (float)(point.Y * scale));
    }

    private static ViewportMouseButton MapButton(PointerUpdateKind kind)
    {
        return kind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => ViewportMouseButton.Left,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => ViewportMouseButton.Middle,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => ViewportMouseButton.Right,
            _ => ViewportMouseButton.None,
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var keyEvent = ToKeyEvent(e, isDown: true);
        _renderer?.OnKeyEvent(keyEvent);
        KeyEventReceived?.Invoke(keyEvent);
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        var keyEvent = ToKeyEvent(e, isDown: false);
        _renderer?.OnKeyEvent(keyEvent);
        KeyEventReceived?.Invoke(keyEvent);
        e.Handled = true;
    }

    private static ViewportKeyEvent ToKeyEvent(KeyEventArgs e, bool isDown)
    {
        return new ViewportKeyEvent
        {
            Key = e.Key.ToString(),
            IsDown = isDown,
            Control = e.KeyModifiers.HasFlag(KeyModifiers.Control),
            Shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift),
        };
    }
}
