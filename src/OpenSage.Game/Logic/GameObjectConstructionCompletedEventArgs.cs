using System;
using OpenSage.Logic.Object;

namespace OpenSage.Logic;

public sealed class GameObjectConstructionCompletedEventArgs : EventArgs
{
    public GameObject GameObject { get; }

    internal GameObjectConstructionCompletedEventArgs(GameObject gameObject)
    {
        GameObject = gameObject;
    }
}
