using System;
using Panopticon.Logic.Object;

namespace Panopticon.Logic;

public sealed class GameObjectConstructionCompletedEventArgs : EventArgs
{
    public GameObject GameObject { get; }

    internal GameObjectConstructionCompletedEventArgs(GameObject gameObject)
    {
        GameObject = gameObject;
    }
}
