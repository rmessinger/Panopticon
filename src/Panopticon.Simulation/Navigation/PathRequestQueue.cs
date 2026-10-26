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

    /// <summary>Requests submitted but not yet terminal (completed or cancelled): the backlog a producer can observe.</summary>
    public long Outstanding
    {
        get
        {
            lock (_gate)
            {
                return _submittedCount - _completedCount - _cancelledCount;
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

            var searchStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var waypoints = _navigation.ComputePath(request.Snapshot, request.Start, request.End, out var endIsPassable);
            _navigation.AddAsyncSearchMeasurement(System.Diagnostics.Stopwatch.GetTimestamp() - searchStart);

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
