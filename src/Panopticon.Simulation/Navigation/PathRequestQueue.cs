#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Panopticon.Logic.Object;

namespace Panopticon.Navigation;

/// <summary>
/// Lifecycle of an async path request (WI-B4-05). <see cref="Applied"/> means the logic thread
/// has observed the completion during a <see cref="PathRequestQueue.Drain"/> call.
/// </summary>
public enum PathRequestState
{
    Queued,
    Computing,
    Completed,
    Cancelled,
    Applied,
}

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

/// <summary>
/// Card WI-B4-05: bounded async pathfinding beside the synchronous <see cref="Navigation"/>
/// API. A single background worker computes requests in submission order against
/// submission-time passability snapshots; the logic thread applies completions in the same
/// order during <see cref="Drain"/>, which the engine calls once per tick. Head-of-line
/// waiting requests block later completions, keeping application order deterministic. The
/// frame in which a completion lands depends on worker wall-clock time by design; consumers
/// must be timing-insensitive.
/// </summary>
public sealed class PathRequestQueue
{
    public const int DefaultFrameBound = 30;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Navigation _navigation;
    private readonly int _maxCompletionFrames;
    private readonly object _gate = new();
    private readonly List<PathRequest> _requests = [];
    private readonly Queue<PathRequest> _work = [];
    private readonly List<(PathRequestHandle Handle, Action<PathRequestHandle> Callback)> _drainCallbacks = [];
    private Graph? _currentTickSnapshot;
    private Thread? _worker;
    private bool _disposed;
    private int _prefixIndex;
    private long _submittedCount;
    private long _completedCount;
    private long _cancelledCount;
    private long _appliedTotal;
    private long _deadlineMisses;

    internal PathRequestQueue(Navigation navigation, int maxCompletionFrames)
    {
        _navigation = navigation;
        _maxCompletionFrames = maxCompletionFrames;
    }

    public long SubmittedCount
    {
        get
        {
            lock (_gate)
            {
                return _submittedCount;
            }
        }
    }

    public long CompletedCount
    {
        get
        {
            lock (_gate)
            {
                return _completedCount;
            }
        }
    }

    public long CancelledCount
    {
        get
        {
            lock (_gate)
            {
                return _cancelledCount;
            }
        }
    }

    public long AppliedCount
    {
        get
        {
            lock (_gate)
            {
                return _appliedTotal;
            }
        }
    }

    public long DeadlineMisses
    {
        get
        {
            lock (_gate)
            {
                return _deadlineMisses;
            }
        }
    }

    internal PathRequestHandle Submit(Vector3 start, Vector3 end, GameObject? requester, Action<PathRequestHandle>? onCompleted)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PathRequestQueue));
            }

            // Same-tick submissions share one snapshot; Drain rotates it so the next tick
            // clones the graph with any passability changes applied since.
            _currentTickSnapshot ??= _navigation.CloneGraphForWorker();

            var request = new PathRequest
            {
                Start = start,
                End = end,
                Requester = requester,
                Callback = onCompleted,
                Snapshot = _currentTickSnapshot,
            };

            var handle = new PathRequestHandle(request, _gate);
            request.Handle = handle;

            _requests.Add(request);
            _work.Enqueue(request);
            _submittedCount++;

            if (_worker is null)
            {
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "Panopticon.PathRequests",
                };
                _worker.Start();
            }

            Monitor.Pulse(_gate);

            return handle;
        }
    }

    /// <summary>
    /// Applies completed requests on the logic thread, in submission order. Called once per
    /// tick by the engine; safe to call with nothing pending.
    /// </summary>
    public void Drain()
    {
        _drainCallbacks.Clear();

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _currentTickSnapshot = null;

            while (_prefixIndex < _requests.Count)
            {
                var request = _requests[_prefixIndex];

                if (request.CancelRequested || request.Requester?.IsDestroyed == true)
                {
                    request.State = PathRequestState.Cancelled;
                    _cancelledCount++;
                    _prefixIndex++;

                    continue;
                }

                if (request.State == PathRequestState.Completed)
                {
                    request.State = PathRequestState.Applied;

                    if (request.WaitedFrames > _maxCompletionFrames)
                    {
                        _deadlineMisses++;
                    }

                    _appliedTotal++;

                    if (request.Callback is not null && request.Handle is not null)
                    {
                        _drainCallbacks.Add((request.Handle, request.Callback));
                    }

                    _prefixIndex++;

                    continue;
                }

                // A queued or computing head-of-line request blocks later completions so
                // application order always matches submission order.
                break;
            }

            // Age unresolved requests after resolution so newly applied ones do not age
            // on the tick they land.
            for (var i = _prefixIndex; i < _requests.Count; i++)
            {
                _requests[i].WaitedFrames++;
            }

            // Bounded memory: drop the resolved prefix once everything is resolved.
            if (_prefixIndex == _requests.Count && _prefixIndex > 64)
            {
                _requests.Clear();
                _prefixIndex = 0;
            }
        }

        // Callbacks run outside the gate: they may submit or cancel, which take the gate.
        foreach (var (handle, callback) in _drainCallbacks)
        {
            callback(handle);
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            PathRequest? request;

            lock (_gate)
            {
                while (_work.Count == 0)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    Monitor.Wait(_gate, 500);

                    if (_disposed)
                    {
                        return;
                    }
                }

                request = _work.Dequeue();
                request.State = PathRequestState.Computing;
            }

            Compute(request);
        }
    }

    private void Compute(PathRequest request)
    {
        try
        {
            // Racy hint check; Drain re-checks authoritatively before applying.
            if (request.CancelRequested || request.Requester?.IsDestroyed == true)
            {
                lock (_gate)
                {
                    request.State = PathRequestState.Cancelled;
                    _cancelledCount++;
                }

                return;
            }

            var waypoints = _navigation.ComputePath(request.Snapshot, request.Start, request.End, out var endIsPassable);

            lock (_gate)
            {
                request.Waypoints = [.. waypoints];
                request.EndIsPassable = endIsPassable;
                request.State = PathRequestState.Completed;
                _completedCount++;
            }
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, $"Async path request from {request.Start} to {request.End} failed.");

            lock (_gate)
            {
                request.State = PathRequestState.Cancelled;
                _cancelledCount++;
            }
        }
    }

    internal void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Monitor.Pulse(_gate);
        }
    }
}
