using System;
using Panopticon.Logic.Object;

namespace Panopticon.Logic;

public sealed class GameObjectDestroyedEventArgs : EventArgs
{
    public GameObject GameObject { get; }

    internal GameObjectDestroyedEventArgs(GameObject gameObject)
    {
        GameObject = gameObject;
    }
}
