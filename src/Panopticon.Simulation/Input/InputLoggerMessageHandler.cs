using System;
using Panopticon.Mathematics;

namespace Panopticon.Input;

public sealed class InputLoggerMessageHandler : InputMessageHandler, IDisposable
{
    private readonly InputLogger _logger = new InputLogger();
    public override HandlingPriority Priority => HandlingPriority.Disabled; // Use lowest available

    public override InputMessageResult HandleMessage(InputMessage message, in TimeInterval gameTime)
    {
        switch (message.MessageType)
        {
            case InputMessageType.KeyDown:
                _logger.LogKeyboardInput(message.Value.Key.ToString(), true);
                break;
            case InputMessageType.KeyUp:
                _logger.LogKeyboardInput(message.Value.Key.ToString(), false);
                break;
            case InputMessageType.MouseLeftButtonDown:
                _logger.LogMouseInput(message.Value.MousePosition.X, message.Value.MousePosition.Y, "Left", true);
                break;
            case InputMessageType.MouseLeftButtonUp:
                _logger.LogMouseInput(message.Value.MousePosition.X, message.Value.MousePosition.Y, "Left", false);
                break;
            case InputMessageType.MouseRightButtonDown:
                _logger.LogMouseInput(message.Value.MousePosition.X, message.Value.MousePosition.Y, "Right", true);
                break;
            case InputMessageType.MouseRightButtonUp:
                _logger.LogMouseInput(message.Value.MousePosition.X, message.Value.MousePosition.Y, "Right", false);
                break;
            case InputMessageType.MouseMiddleButtonDown:
                _logger.LogMouseInput(message.Value.MousePosition.X, message.Value.MousePosition.Y, "Middle", true);
                break;
            case InputMessageType.MouseMiddleButtonUp:
                _logger.LogMouseInput(message.Value.MousePosition.X, message.Value.MousePosition.Y, "Middle", false);
                break;
            case InputMessageType.MouseMove:
                _logger.LogMouseInput(message.Value.MousePosition.X, message.Value.MousePosition.Y);
                break;
            case InputMessageType.MouseWheel:
                _logger.LogMouseInput(0, 0, null, false, message.Value.ScrollWheel);
                break;
        }

        return InputMessageResult.NotHandled;
    }

    public void Dispose()
    {
        _logger.Dispose();
    }
}
