using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Panopticon.Input;
using Panopticon.Mathematics;
using Panopticon.Utilities;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon;

public sealed class GameWindow : DisposableBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    public event EventHandler ClientSizeChanged;

    private readonly Sdl2Window _window;

    private bool _closing;
    private int _lastMouseX;
    private int _lastMouseY;

    // Trackpad scroll smoothing: accumulate fractional deltas and debounce direction reversals
    // to prevent rapid +1/-1 oscillation from macOS trackpad inertia physics.
    private float _scrollAccumulator;
    private int _lastScrollDirection;

    private void RaiseClientSizeChanged()
    {
        ClientSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    public Swapchain Swapchain { get; private set; }

    public Rectangle ClientBounds
    {
        get
        {
            var result = _window.Bounds;
            return new Rectangle(0, 0, result.Width, result.Height);
        }
    }

    public bool IsCursorVisible
    {
        get => _window.CursorVisible;
        set => _window.CursorVisible = value;
    }

    public float WindowScale { get; private set; }

    // On macOS the cursor appears 25% larger than intended, so scale it down.
    private const float MacOsCursorScaleFactor = 0.75f;
    public float CursorScale => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? WindowScale * MacOsCursorScaleFactor
        : WindowScale;

    public InputSnapshot CurrentInputSnapshot { get; private set; }

    public Queue<InputMessage> MessageQueue { get; } = new Queue<InputMessage>();

    public bool Fullscreen
    {
        get => _window.WindowState == WindowState.BorderlessFullScreen;
        set => _window.WindowState = value ? WindowState.BorderlessFullScreen : WindowState.Normal;
    }

    public bool Maximized
    {
        get { return _window.WindowState == WindowState.Maximized; }
        set { _window.WindowState = value ? WindowState.Maximized : WindowState.Normal; }
    }

    public GameWindow(string title, int x, int y, int width, int height, bool fullscreen)
    {
        var windowState = fullscreen ? WindowState.BorderlessFullScreen : WindowState.Normal;

        // Get display scale for primary monitor.
        // TODO: Track moving window to a different display,
        // which may change the scale.
        WindowScale = Sdl2Interop.GetDisplayScale(0);

        var windowCreateInfo = new WindowCreateInfo(
            (int)(WindowScale * x),
            (int)(WindowScale * y),
            (int)(WindowScale * width),
            (int)(WindowScale * height),
            windowState,
            title);

        _window = VeldridStartup.CreateWindow(ref windowCreateInfo);

        Logger.Debug($"[GameWindow] WindowScale={WindowScale}, requested={width}x{height}, actual Bounds={_window.Bounds.Width}x{_window.Bounds.Height}");

        _window.KeyDown += HandleKeyDown;
        _window.KeyUp += HandleKeyUp;

        _window.MouseDown += HandleMouseDown;
        _window.MouseUp += HandleMouseUp;
        _window.MouseMove += HandleMouseMove;
        _window.MouseWheel += HandleMouseWheel;

        _window.Resized += HandleResized;

        _window.Closing += HandleClosing;
    }

    public GraphicsDevice CreateGraphicsDevice(GraphicsDeviceOptions options, GraphicsBackend backend)
    {
        var graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window, options, backend);
        Swapchain = graphicsDevice.MainSwapchain;
        return graphicsDevice;
    }

    private void HandleClosing()
    {
        _closing = true;
    }

    private void HandleResized()
    {
        var bounds = _window.Bounds;
        Swapchain.Resize(
            (uint)bounds.Width,
            (uint)bounds.Height);

        RaiseClientSizeChanged();
    }

    private void HandleKeyDown(KeyEvent evt)
    {
        var message = InputMessage.CreateKeyDown(evt.Key, evt.Modifiers);
        MessageQueue.Enqueue(message);
    }

    private void HandleKeyUp(KeyEvent evt)
    {
        var message = InputMessage.CreateKeyUp(evt.Key, evt.Modifiers);
        MessageQueue.Enqueue(message);
    }

    private void HandleMouseDown(MouseEvent evt)
    {
        InputMessageType? getMessageType()
        {
            switch (evt.MouseButton)
            {
                case MouseButton.Left:
                    return InputMessageType.MouseLeftButtonDown;
                case MouseButton.Middle:
                    return InputMessageType.MouseMiddleButtonDown;
                case MouseButton.Right:
                    return InputMessageType.MouseRightButtonDown;
                default:
                    return null;
            }
        }

        var messageType = getMessageType();
        if (messageType == null)
        {
            return;
        }

        var message = InputMessage.CreateMouseButton(messageType.Value, new Point2D(_lastMouseX, _lastMouseY));
        MessageQueue.Enqueue(message);
    }

    private void HandleMouseUp(MouseEvent evt)
    {
        InputMessageType? getMessageType()
        {
            switch (evt.MouseButton)
            {
                case MouseButton.Left:
                    return InputMessageType.MouseLeftButtonUp;
                case MouseButton.Middle:
                    return InputMessageType.MouseMiddleButtonUp;
                case MouseButton.Right:
                    return InputMessageType.MouseRightButtonUp;
                default:
                    return null;
            }
        }

        var messageType = getMessageType();
        if (messageType == null)
        {
            return;
        }

        var message = InputMessage.CreateMouseButton(messageType.Value, new Point2D(_lastMouseX, _lastMouseY));
        MessageQueue.Enqueue(message);
    }

    private void HandleMouseMove(MouseMoveEventArgs args)
    {
        _lastMouseX = args.State.X;
        _lastMouseY = args.State.Y;

        var message = InputMessage.CreateMouseMove(new Point2D(args.State.X, args.State.Y));
        MessageQueue.Enqueue(message);
    }

    private void HandleMouseWheel(MouseWheelEventArgs args)
    {
        var delta = args.WheelDelta;
        if (delta == 0)
        {
            return;
        }

        var deltaSign = Math.Sign(delta);

        // Debounce rapid direction reversals caused by macOS trackpad inertia/bounce physics.
        // A real mouse wheel fully consumes its accumulator every notch (delta is a whole
        // number), so the accumulator is back to zero before the next event arrives. Trackpad
        // inertia instead leaves a fractional remainder that then flips sign as it settles.
        // Only discard the reversal when there's a pending fractional remainder, so a
        // deliberate mouse-wheel direction change isn't dropped.
        if (_lastScrollDirection != 0 && deltaSign != _lastScrollDirection && _scrollAccumulator != 0)
        {
            _scrollAccumulator = 0;
            _lastScrollDirection = 0;
            return;
        }

        _lastScrollDirection = deltaSign;
        _scrollAccumulator += delta;

        var discreteAmount = (int)_scrollAccumulator;
        if (discreteAmount != 0)
        {
            _scrollAccumulator -= discreteAmount;
            MessageQueue.Enqueue(InputMessage.CreateMouseWheel(discreteAmount));
        }
    }

    public void Close()
    {
        _closing = true;
    }

    public bool PumpEvents()
    {
        CurrentInputSnapshot = _window.PumpEvents();

        if (_closing)
        {
            return false;
        }

        return true;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        // TODO: This isn't right.
        _window.Close();

        base.Dispose(disposeManagedResources);
    }
}
