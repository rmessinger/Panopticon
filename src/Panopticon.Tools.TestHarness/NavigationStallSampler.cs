#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic.Object;
using Panopticon.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>A unit's worst observed stall state; fields snapshot one sample in time.</summary>
public sealed record NavigationStallRecord(
    uint UnitId,
    string Definition,
    string Reason,
    uint BlockerId,
    int FramesWithoutProgress,
    Vector3 OrderDestination,
    uint Frame);

/// <summary>A wait-for cycle: each unit is blocked by the next, the last by the first.</summary>
public sealed record NavigationWaitForCycle(IReadOnlyList<uint> UnitIds);

/// <summary>
/// Card WI-N6-01: samples per-unit movement stalls and derives wait-for cycles from blocker
/// ids. Records keep each unit's worst frames-without-progress sample for end-of-run
/// attribution; cycles reflect the latest sample. Detection only; no resolution behavior.
/// </summary>
public sealed class NavigationStallSampler
{
    private readonly Dictionary<uint, NavigationStallRecord> _worstByUnit = new();
    private readonly List<NavigationStallRecord> _current = new();
    private readonly List<NavigationWaitForCycle> _cycles = new();

    public IReadOnlyList<NavigationStallRecord> Records => _worstByUnit.Values.OrderBy(record => record.UnitId).ToList();

    /// <summary>Stalls in the latest sample (WI-N7-01): the live set, without worst-case
    /// retention, so a unit that settled stops appearing.</summary>
    public IReadOnlyList<NavigationStallRecord> CurrentStalls => _current;

    public IReadOnlyList<NavigationWaitForCycle> WaitForCycles => _cycles;

    public void Sample(uint frame, IEnumerable<GameObject> units)
    {
        _current.Clear();
        foreach (var unit in units)
        {
            var progress = unit.AIUpdate?.Progress;
            if (progress == null
                || (progress.Reason == NavigationStallReason.None && progress.FramesWithoutProgress == 0))
            {
                continue;
            }

            var orderDestination = unit.AIUpdate.TargetPoints.Count > 0
                ? unit.AIUpdate.TargetPoints[^1]
                : unit.Translation;
            var record = new NavigationStallRecord(
                unit.Id.Index,
                unit.Definition.Name,
                progress.Reason.ToString(),
                progress.BlockerId.Index,
                progress.FramesWithoutProgress,
                orderDestination,
                frame);
            _current.Add(record);
            if (!_worstByUnit.TryGetValue(record.UnitId, out var worst)
                || record.FramesWithoutProgress > worst.FramesWithoutProgress)
            {
                _worstByUnit[record.UnitId] = record;
            }
        }

        BuildWaitForCycles();
    }

    // Each stalled unit blocks on at most one object, so the wait-for graph is functional:
    // walking each chain once finds every cycle without a full graph search.
    private void BuildWaitForCycles()
    {
        _cycles.Clear();
        var stalled = new Dictionary<uint, NavigationStallRecord>();
        foreach (var record in _current)
        {
            stalled[record.UnitId] = record;
        }

        var next = new Dictionary<uint, uint>();
        foreach (var record in _current)
        {
            if (record.Reason == nameof(NavigationStallReason.BlockedByUnit)
                && stalled.ContainsKey(record.BlockerId))
            {
                next[record.UnitId] = record.BlockerId;
            }
        }

        var walked = new HashSet<uint>();
        foreach (var unitId in next.Keys.OrderBy(id => id))
        {
            if (walked.Contains(unitId))
            {
                continue;
            }

            var chain = new List<uint>();
            var chainIndex = new Dictionary<uint, int>();
            var current = unitId;
            while (next.TryGetValue(current, out var blocker)
                && !walked.Contains(current)
                && !chainIndex.ContainsKey(current))
            {
                chainIndex[current] = chain.Count;
                chain.Add(current);
                current = blocker;
            }

            foreach (var id in chain)
            {
                walked.Add(id);
            }

            if (chainIndex.TryGetValue(current, out var cycleStart))
            {
                _cycles.Add(new NavigationWaitForCycle(chain.Skip(cycleStart).ToArray()));
            }
        }
    }
}
