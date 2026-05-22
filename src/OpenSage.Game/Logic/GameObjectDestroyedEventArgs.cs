using System;
using OpenSage.Logic.Object;

namespace OpenSage.Logic;

public sealed class GameObjectDestroyedEventArgs : EventArgs
{
    public GameObject GameObject { get; }

    internal GameObjectDestroyedEventArgs(GameObject gameObject)
    {
        GameObject = gameObject;
    }
}
