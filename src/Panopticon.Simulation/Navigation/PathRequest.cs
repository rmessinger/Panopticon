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

    /// <summary>Submission-time passability snapshot; searched by the worker, never mutated.</summary>
    public Graph Snapshot { get; init; } = null!;

    public List<Vector3>? Waypoints { get; set; }

    public bool EndIsPassable { get; set; }

    public PathRequestState State { get; set; } = PathRequestState.Queued;

    public bool CancelRequested { get; set; }

    public int WaitedFrames { get; set; }

    public PathRequestHandle? Handle { get; set; }
}
