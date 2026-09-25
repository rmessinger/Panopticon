using System;
using Panopticon.Logic.Object;

namespace Panopticon.Logic;

public sealed class GameObjectCreatedEventArgs : EventArgs
{
    public GameObject GameObject { get; }

    internal GameObjectCreatedEventArgs(GameObject gameObject)
    {
        GameObject = gameObject;
    }
}
