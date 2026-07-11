using System;
using OpenSage.Logic.Object;

namespace OpenSage.Logic;

public enum GameObjectLifecycleEventReason
{
    Died,
    Destroyed,
    OwnerChanged,
}

public sealed class GameObjectLifecycleEventArgs : EventArgs
{
    public GameObjectLifecycleEventArgs(
        GameObject gameObject,
        Player previousOwner,
        Player currentOwner,
        GameObjectLifecycleEventReason reason)
    {
        GameObject = gameObject;
        PreviousOwner = previousOwner;
        CurrentOwner = currentOwner;
        Reason = reason;
    }

    public GameObject GameObject { get; }

    public Player PreviousOwner { get; }

    public Player CurrentOwner { get; }

    public GameObjectLifecycleEventReason Reason { get; }
}
