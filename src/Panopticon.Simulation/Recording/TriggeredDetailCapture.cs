#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// T2 triggered detail capture (WI-A1-10): a bounded ring buffer of recent per-frame deltas,
/// tripped live by a T1 trigger (a re-run will not reproduce a divergence, per
/// [a5-visualization.md §5](../../docs/roadmap/deep-dives/a5-visualization.md)). Callers can
/// also pre-mark a frame window. Total windows and per-window depth are bounded.
/// </summary>
public sealed class TriggeredDetailCapture : IDisposable
{
    /// <summary>Default ring depth: one second of Generals logic.</summary>
    public const int DefaultRingDepth = 30;

    /// <summary>Default cap on captured windows, bounding output size.</summary>
    public const int DefaultMaxWindows = 8;

    private readonly WorldStateSampler _sampler;
    private readonly Queue<WorldStateDelta> _ring = new();
    private readonly List<TriggeredDetailWindow> _windows = new();
    private readonly List<WorldStateDelta> _markedFrames = new();
    private readonly Dictionary<uint, string> _lastRegion = new();
    private readonly Func<WorldObjectState, string>? _regionClassifier;
    private readonly HashSet<string>? _regionFilter;
    private bool _hasMarkedWindow;
    private uint _markedStart;
    private uint _markedEnd;

    public int RingDepth { get; }
    public int MaxWindows { get; }
    public uint CooldownFrames { get; }
    public IReadOnlyList<string>? RegionKeys { get; }
    public IReadOnlyList<TriggeredDetailWindow> Windows => _windows;

    private uint _lastTripFrame;
    private bool _full;

    public TriggeredDetailCapture(
        WorldStateSampler sampler,
        int ringDepth = DefaultRingDepth,
        int maxWindows = DefaultMaxWindows,
        uint? cooldownFrames = null,
        Func<WorldObjectState, string>? regionClassifier = null,
        IReadOnlyList<string>? regionKeys = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ringDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWindows);

        _sampler = sampler;
        RingDepth = ringDepth;
        MaxWindows = maxWindows;
        CooldownFrames = cooldownFrames ?? (uint)ringDepth;
        _regionClassifier = regionClassifier;
        RegionKeys = regionKeys is { Count: > 0 } ? regionKeys : null;
        _regionFilter = RegionKeys is null ? null : new HashSet<string>(RegionKeys, StringComparer.Ordinal);
        _sampler.FrameSampled += OnFrameSampled;
    }

    /// <summary>
    /// WI-A5-21: builds a capture from caller options, wiring the marked window when both its
    /// bounds are set.
    /// </summary>
    public static TriggeredDetailCapture Create(
        WorldStateSampler sampler,
        TriggeredDetailCaptureOptions options,
        Func<WorldObjectState, string>? regionClassifier = null)
    {
        var capture = new TriggeredDetailCapture(
            sampler,
            options.RingDepth ?? DefaultRingDepth,
            options.MaxWindows ?? DefaultMaxWindows,
            options.CooldownFrames,
            regionClassifier,
            options.RegionKeys)
        {
            _markedStart = options.MarkedStartFrame ?? 0,
            _markedEnd = options.MarkedEndFrame ?? 0,
            _hasMarkedWindow = options.MarkedStartFrame is not null && options.MarkedEndFrame is not null,
        };
        return capture;
    }

    /// <summary>
    /// Pre-marks a frame window for detail capture regardless of any trigger
    /// (§5.2: "a user can also pre-mark a frame/region window").
    /// </summary>
    public static TriggeredDetailCapture WithMarkedWindow(
        WorldStateSampler sampler,
        uint startFrame,
        uint endFrame,
        int ringDepth = DefaultRingDepth,
        int maxWindows = DefaultMaxWindows)
    {
        var capture = new TriggeredDetailCapture(sampler, ringDepth, maxWindows)
        {
            _markedStart = startFrame,
            _markedEnd = endFrame,
            _hasMarkedWindow = true,
        };
        return capture;
    }

    private void OnFrameSampled(WorldStateSummary summary, WorldStateDelta delta)
    {
        // Ring buffer: keep the most recent RingDepth frames.
        _ring.Enqueue(delta);
        while (_ring.Count > RingDepth)
        {
            _ring.Dequeue();
        }

        // Region bookkeeping for filtering: track where each live entity currently sits. Bounded
        // by the live entity count and zero-cost when no filter is configured (WI-A5-21).
        if (_regionFilter is not null && _regionClassifier is not null)
        {
            foreach (var added in delta.Added)
            {
                _lastRegion[added.ObjectId] = _regionClassifier(added);
            }

            foreach (var changed in delta.Changed)
            {
                _lastRegion[changed.ObjectId] = _regionClassifier(changed);
            }

            foreach (var removed in delta.Removed)
            {
                _lastRegion.Remove(removed);
            }
        }

        // Marked window collection.
        if (_hasMarkedWindow && delta.Frame >= _markedStart && delta.Frame <= _markedEnd)
        {
            _markedFrames.Add(delta);
        }
    }

    private WorldStateDelta ProjectToRegions(WorldStateDelta delta)
    {
        if (_regionFilter is null || _regionClassifier is null)
        {
            return delta;
        }

        var projected = new WorldStateDelta { Frame = delta.Frame };
        projected.PlayerMoney.AddRange(delta.PlayerMoney);
        projected.Added.AddRange(delta.Added.Where(o => _regionFilter.Contains(_regionClassifier(o))));
        projected.Changed.AddRange(delta.Changed.Where(o => _regionFilter.Contains(_regionClassifier(o))));

        // Removals carry only an id: classify through the last known region, keeping unknowns so
        // a filter never hides a deletion (conservative include, WI-A5-21).
        projected.Removed.AddRange(delta.Removed.Where(id =>
            !_lastRegion.TryGetValue(id, out var region) || _regionFilter.Contains(region)));

        return projected;
    }

    /// <summary>
    /// Captures the current ring buffer as a trigger window. Cooldown between trips keeps a
    /// persistently-diverging run from filling every window with the same region; the first trip
    /// is always allowed.
    /// </summary>
    internal void Trip(uint frame, SignatureTreeFrame tree, IReadOnlyList<string>? divergedSubtrees = null)
    {
        if (_full || (_lastTripFrame != 0 && frame < _lastTripFrame + CooldownFrames))
        {
            return;
        }

        var frames = new List<WorldStateDelta>(_ring.Count);
        var entityIds = new HashSet<uint>();
        foreach (var delta in _ring)
        {
            var projected = ProjectToRegions(delta);
            frames.Add(projected);
            foreach (var added in projected.Added)
            {
                entityIds.Add(added.ObjectId);
            }

            foreach (var changed in projected.Changed)
            {
                entityIds.Add(changed.ObjectId);
            }

            foreach (var removed in projected.Removed)
            {
                entityIds.Add(removed);
            }
        }

        var window = new TriggeredDetailWindow
        {
            TriggeredAtFrame = frame,
            DivergedSubtrees = divergedSubtrees,
            RingDepth = RingDepth,
            MaxWindows = MaxWindows,
            CooldownFrames = CooldownFrames,
            RegionKeys = RegionKeys,
            CapturedEntityCount = entityIds.Count,
        };
        window.Frames.AddRange(frames);
        _windows.Add(window);
        _lastTripFrame = frame;
        _full = _windows.Count >= MaxWindows;
    }

    /// <summary>Finishes collection: flushes the marked window, if any, into the window list.</summary>
    public IReadOnlyList<TriggeredDetailWindow> Complete()
    {
        if (_hasMarkedWindow && _markedFrames.Count > 0)
        {
            var projectedFrames = _markedFrames.Select(ProjectToRegions).ToList();
            var entityIds = new HashSet<uint>();
            foreach (var delta in projectedFrames)
            {
                foreach (var added in delta.Added)
                {
                    entityIds.Add(added.ObjectId);
                }

                foreach (var changed in delta.Changed)
                {
                    entityIds.Add(changed.ObjectId);
                }

                foreach (var removed in delta.Removed)
                {
                    entityIds.Add(removed);
                }
            }

            var window = new TriggeredDetailWindow
            {
                Marked = true,
                TriggeredAtFrame = projectedFrames[0].Frame,
                Reason = "marked",
                RingDepth = RingDepth,
                MaxWindows = MaxWindows,
                CooldownFrames = CooldownFrames,
                RegionKeys = RegionKeys,
                CapturedEntityCount = entityIds.Count,
            };
            window.Frames.AddRange(projectedFrames);
            _windows.Add(window);
        }

        return _windows;
    }

    public void Dispose() => _sampler.FrameSampled -= OnFrameSampled;
}
