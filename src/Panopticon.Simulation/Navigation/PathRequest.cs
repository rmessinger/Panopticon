#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Navigation;

/// <summary>Internal record of one submitted path request and its result.</summary>
internal sealed class PathRequest
{
    public Vector3 Start { get; init; }

    public Vector3 End { get; init; }

    public GameObject? Requester { get; init; }

    public Action<PathRequestHandle>? Callback { get; init; }

    /// <summary>Submission-time passability snapshot (WI-N1-03); the worker applies its cells
    /// to its persistent graph when the version differs from the last applied one.</summary>
    public PassabilitySnapshot Snapshot { get; init; } = null!;

    public List<Vector3>? Waypoints { get; set; }

    public bool EndIsPassable { get; set; }

    /// <summary>WI-N7-01: the worker died computing this request; it completes empty with a
    /// Failed outcome so the drain applies it and the callback can settle the requester.</summary>
    public bool Failed { get; set; }

    public PathRequestState State { get; set; } = PathRequestState.Queued;

    public bool CancelRequested { get; set; }

    public int WaitedFrames { get; set; }

    public PathRequestHandle? Handle { get; set; }
}
