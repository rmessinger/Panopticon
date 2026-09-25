using System;

namespace Panopticon.Input;

public sealed class InputMessageEventArgs : EventArgs
{
    public InputMessage Message { get; }

    public InputMessageEventArgs(InputMessage message)
    {
        Message = message;
    }
}
