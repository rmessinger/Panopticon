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
/// Terminal outcome of a request as seen through a <see cref="PathRequestHandle"/> (WI-N7-01).
/// <see cref="PathRequestOutcome.Failed"/> is a worker exception surfaced as a completion with
/// no waypoints, so the drain applies it and the callback settles the requester.
/// </summary>
public enum PathRequestOutcome
{
    Pending,
    Completed,
    Failed,
    Cancelled,
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

    // Worker-thread search scratch (WI-N1-01): reused across requests; Waypoints copies out
    // under the lock, which stays the single per-request allocation.
    private readonly List<Vector3> _waypointBuffer = new();
    private readonly object _gate = new();
    private readonly List<PathRequest> _requests = [];
    private readonly Queue<PathRequest> _work = [];
    private readonly List<(PathRequestHandle Handle, Action<PathRequestHandle> Callback)> _drainCallbacks = [];
    private PassabilitySnapshot _currentSnapshot;
    private Thread? _worker;
    private bool _disposed;
    private int _prefixIndex;
    private long _submittedCount;
    private long _completedCount;
    private long _cancelledCount;
    private long _appliedTotal;
    private long _deadlineMisses;
    private readonly List<int> _waitFrameSamples = new();

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

    /// <summary>WI-N4-02: waited-frames sample per applied request, in apply order. Callers
    /// derive run-level median/p99/max wait statistics.</summary>
    public int[] CopyWaitFrameSamples()
    {
        lock (_gate)
        {
            return [.. _waitFrameSamples];
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

            // WI-N1-03: submissions share the snapshot until the passability version changes;
            // a stable grid pays zero snapshot cost instead of a per-tick graph clone.
            var version = _navigation.PassabilityVersion;

            if (_currentSnapshot is null || _currentSnapshot.Version != version)
            {
                _currentSnapshot = _navigation.BuildPassabilitySnapshot();
            }

            var request = new PathRequest
            {
                Start = start,
                End = end,
                Requester = requester,
                Callback = onCompleted,
                Snapshot = _currentSnapshot,
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

            while (_prefixIndex < _requests.Count)
            {
                var request = _requests[_prefixIndex];

                if (request.CancelRequested || request.Requester?.IsDestroyed == true)
                {
                    // Count each request exactly once toward Outstanding: the worker may have
                    // marked it cancelled already, or completed it before the cancel landed
                    // (completions that race a cancel are discarded here, never applied).
                    if (request.State is not (PathRequestState.Cancelled or PathRequestState.Completed))
                    {
                        _cancelledCount++;
                    }

                    request.State = PathRequestState.Cancelled;
                    _prefixIndex++;

                    continue;
                }

                if (request.State == PathRequestState.Completed)
                {
                    request.State = PathRequestState.Applied;
                    _waitFrameSamples.Add(request.WaitedFrames);

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

            ManualResetEventSlim? block;
            lock (_gate)
            {
                block = _workerBlock;
            }

            block?.Wait();

            Compute(request);
        }
    }

    // Worker-thread-only state (WI-N1-03): one persistent search graph whose passability is
    // re-applied only when a request carries a version newer than the last applied one. Only
    // WorkerLoop/Compute touch these, so no lock is needed.
    private Graph _workerGraph;
    private int _appliedVersion;

    // WI-N6-02: test seam that makes the next computed request die the way an unexpected
    // worker exception would (cancelled without a callback), reproducing the deadlocked
    // awaiting unit. Set before submitting; consumed by the next Compute call.
    private volatile bool _failNextCompute;

    internal void ForceNextWorkerFailure() => _failNextCompute = true;

    // WI-N7-01: test seam that parks the worker before its next Compute, so a request can be
    // held unresolved and aged past the pending-path watchdog deterministically.
    private ManualResetEventSlim? _workerBlock;

    internal void BlockWorker()
    {
        lock (_gate)
        {
            _workerBlock ??= new ManualResetEventSlim(false);
        }
    }

    internal void ReleaseWorker()
    {
        lock (_gate)
        {
            _workerBlock?.Set();
        }
    }

    private void Compute(PathRequest request)
    {
        try
        {
            if (_failNextCompute)
            {
                _failNextCompute = false;
                throw new InvalidOperationException("Injected async path worker failure (WI-N6-02).");
            }

            // Racy hint check; Drain re-checks authoritatively before applying.
            if (request.CancelRequested || request.Requester?.IsDestroyed == true)
            {
                lock (_gate)
                {
                    // Drain may already have marked this cancelled; count exactly once.
                    if (request.State != PathRequestState.Cancelled)
                    {
                        _cancelledCount++;
                    }

                    request.State = PathRequestState.Cancelled;
                }

                return;
            }

            if (request.Snapshot.Version != _appliedVersion)
            {
                _workerGraph ??= _navigation.CloneGraphForWorker();
                _workerGraph.ApplyPassability(request.Snapshot.Cells);
                _workerGraph.MarkRegionsDirty();
                _appliedVersion = request.Snapshot.Version;
            }

            var searchStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _navigation.ComputePath(_workerGraph, request.Start, request.End, out var endIsPassable, _waypointBuffer);
            _navigation.AddAsyncSearchMeasurement(System.Diagnostics.Stopwatch.GetTimestamp() - searchStart);

            lock (_gate)
            {
                // Re-check under the gate: Drain may have cancelled this while it was computing.
                // A raced completion is discarded, and each request resolves exactly one counter.
                if (request.CancelRequested || request.Requester?.IsDestroyed == true)
                {
                    if (request.State != PathRequestState.Cancelled)
                    {
                        _cancelledCount++;
                    }

                    request.State = PathRequestState.Cancelled;

                    return;
                }

                request.Waypoints = [.. _waypointBuffer];
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
                if (request.CancelRequested || request.Requester?.IsDestroyed == true
                    || request.State == PathRequestState.Cancelled)
                {
                    if (request.State != PathRequestState.Cancelled)
                    {
                        _cancelledCount++;
                    }

                    request.State = PathRequestState.Cancelled;

                    return;
                }

                // WI-N7-01: a worker exception completes the request as Failed (an empty
                // completion with a Failed outcome) so the drain applies it in order and the
                // callback settles the requester instead of leaving it awaiting forever.
                request.Failed = true;
                request.Waypoints = [];
                request.EndIsPassable = false;
                request.State = PathRequestState.Completed;
                _completedCount++;
            }
        }
    }

    internal void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _workerBlock?.Set();
            Monitor.Pulse(_gate);
        }
    }
}
