#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Navigation;

/// <summary>
/// Caller-facing handle for one async path request. All members are safe from any thread;
/// results become visible after the owning <see cref="Navigation"/> drains completions on the
/// logic thread.
/// </summary>
public sealed class PathRequestHandle
{
    private readonly object _gate;
    private readonly PathRequest _request;

    internal PathRequestHandle(PathRequest request, object gate)
    {
        _gate = gate;
        _request = request;
    }

    public bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                return _request.State is PathRequestState.Completed or PathRequestState.Applied;
            }
        }
    }

    public bool IsCancelled
    {
        get
        {
            lock (_gate)
            {
                return _request.State is PathRequestState.Cancelled;
            }
        }
    }

    /// <summary>WI-N7-01: terminal outcome of this request; Failed means the worker died
    /// computing it and the completion carries no waypoints.</summary>
    public PathRequestOutcome Outcome
    {
        get
        {
            lock (_gate)
            {
                return _request.State switch
                {
                    PathRequestState.Completed or PathRequestState.Applied
                        => _request.Failed ? PathRequestOutcome.Failed : PathRequestOutcome.Completed,
                    PathRequestState.Cancelled => PathRequestOutcome.Cancelled,
                    _ => PathRequestOutcome.Pending,
                };
            }
        }
    }

    /// <summary>WI-N7-01: ticks this request has spent unresolved in the queue; aged by the
    /// drain. Input for the pending-path watchdog.</summary>
    public int WaitedFrames
    {
        get
        {
            lock (_gate)
            {
                return _request.WaitedFrames;
            }
        }
    }

    /// <summary>Returns the computed waypoints once the request completed or was applied.</summary>
    public bool TryGetResult(out IList<Vector3> waypoints, out bool endIsPassable)
    {
        lock (_gate)
        {
            if (_request.State is not (PathRequestState.Completed or PathRequestState.Applied))
            {
                waypoints = Array.Empty<Vector3>();
                endIsPassable = false;

                return false;
            }

            waypoints = _request.Waypoints ?? new List<Vector3>();
            endIsPassable = _request.EndIsPassable;

            return true;
        }
    }

    /// <summary>
    /// Requests cancellation. A cancelled request never applies and never invokes its callback;
    /// a completion that raced the cancel is discarded at drain time.
    /// </summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _request.CancelRequested = true;
        }
    }
}
