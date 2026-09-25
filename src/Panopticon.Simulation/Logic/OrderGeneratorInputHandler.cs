using System;
using Panopticon.Input;
using Panopticon.Logic.OrderGenerators;
using Panopticon.Mathematics;

namespace Panopticon.Logic;

public class OrderGeneratorInputHandler : InputMessageHandler
{
    private readonly OrderGeneratorSystem _orderGeneratorSystem;
    private readonly MouseClickMode _mouseClickMode;

    private bool _isDragging;
    private Point2D _mousePosition;
    private Point2D _dragEndPosition;

    // Right-click tracking for Standard (modern) mode.
    private bool _rightButtonTracking;
    private Point2D _rightButtonDownPos;
    private TimeSpan _rightButtonDownTime;

    // A right-click is treated as a short click (order) if it covers fewer than
    // this many pixels squared and is released within ShortClickMaxMs.
    private const int ShortClickMaxDistanceSq = 25; // 5 px radius
    private const double ShortClickMaxMs = 300.0;

    private KeyModifiers _keyModifiers;

    public override HandlingPriority Priority => _priority;
    private HandlingPriority _priority = HandlingPriority.Disabled;

    internal KeyModifiers KeyModifiers => _keyModifiers;

    public OrderGeneratorInputHandler(OrderGeneratorSystem orderGeneratorSystem, MouseClickMode mouseClickMode = MouseClickMode.Standard)
    {
        _orderGeneratorSystem = orderGeneratorSystem;
        _mouseClickMode = mouseClickMode;
    }

    public void Update()
    {
        _priority = HandlingPriority.OrderGeneratorPriority;

        _orderGeneratorSystem.UpdatePosition(_mousePosition.ToVector2());
    }

    public override InputMessageResult HandleMessage(InputMessage message, in TimeInterval gameTime)
    {
        switch (message.MessageType)
        {
            case InputMessageType.MouseMove:
                if (_isDragging)
                {
                    _dragEndPosition = message.Value.MousePosition;
                    _orderGeneratorSystem.UpdateDrag(_dragEndPosition.ToVector2());
                }
                else
                {
                    _mousePosition = message.Value.MousePosition;
                }

                break;

            case InputMessageType.MouseLeftButtonDown:
                if (_mouseClickMode == MouseClickMode.Classic)
                {
                    // Classic: left-click issues orders immediately (or starts a drag).
                    if (!_orderGeneratorSystem.ActiveGenerator.CanDrag)
                    {
                        if (_orderGeneratorSystem.TryActivate(_keyModifiers))
                        {
                            return InputMessageResult.Handled;
                        }

                        break;
                    }
                }

                // Both modes: start a left-drag for generators that support dragging
                // (e.g. ConstructBuildingOrderGenerator).
                if (_orderGeneratorSystem.ActiveGenerator.CanDrag)
                {
                    _isDragging = true;
                    // Copy initial position so the delta is 0 if drag ends immediately.
                    _dragEndPosition = _mousePosition;
                    return InputMessageResult.Handled;
                }

                break;

            case InputMessageType.MouseLeftButtonUp:
                if (_isDragging)
                {
                    _orderGeneratorSystem.TryActivate(_keyModifiers);
                    _isDragging = false;
                    return InputMessageResult.Handled;
                }

                break;

            case InputMessageType.MouseRightButtonDown:
                if (_mouseClickMode == MouseClickMode.Standard)
                {
                    // Cancel any non-default generator (e.g. build placement) on right-click.
                    if (_orderGeneratorSystem.ActiveGenerator is not UnitOrderGenerator)
                    {
                        _orderGeneratorSystem.CancelOrderGenerator();
                    }
                    else
                    {
                        // Start tracking so we can distinguish a short click from a drag.
                        _rightButtonTracking = true;
                        _rightButtonDownPos = message.Value.MousePosition;
                        _rightButtonDownTime = gameTime.TotalTime;
                    }
                }
                else
                {
                    // Classic: right-click cancels the active order generator.
                    _orderGeneratorSystem.CancelOrderGenerator();
                }

                break;

            case InputMessageType.MouseRightButtonUp:
                if (_mouseClickMode == MouseClickMode.Standard && _rightButtonTracking)
                {
                    _rightButtonTracking = false;

                    var pos = message.Value.MousePosition;
                    var dx = pos.X - _rightButtonDownPos.X;
                    var dy = pos.Y - _rightButtonDownPos.Y;
                    var distSq = dx * dx + dy * dy;
                    var elapsed = (gameTime.TotalTime - _rightButtonDownTime).TotalMilliseconds;

                    if (distSq <= ShortClickMaxDistanceSq && elapsed <= ShortClickMaxMs)
                    {
                        // Short right-click: issue the smart order.
                        // Return NotHandled so that SelectionMessageHandler and LookAtTranslator
                        // can also see the up-event and reset their internal state (Panning,
                        // IsRotating) correctly.
                        _orderGeneratorSystem.TryActivate(_keyModifiers);
                    }
                }

                break;

            case InputMessageType.KeyDown:
                if (message.Value.Key == Veldrid.Key.ControlLeft ||
                    message.Value.Key == Veldrid.Key.ControlRight)
                {
                    _keyModifiers |= KeyModifiers.Ctrl;
                    return InputMessageResult.Handled;
                }

                break;

            case InputMessageType.KeyUp:
                if (message.Value.Key == Veldrid.Key.ControlLeft ||
                    message.Value.Key == Veldrid.Key.ControlRight)
                {
                    _keyModifiers &= ~KeyModifiers.Ctrl;
                    return InputMessageResult.Handled;
                }

                break;
        }

        return InputMessageResult.NotHandled;
    }
}
