using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Per-frame observations for one tracked unit, supplied by the host through a read-only seam so
/// <see cref="UnitActionTimer"/> stays in <c>.Core</c> and headlessly testable. Fields not relevant
/// to a unit's action kind are ignored.
/// </summary>
public struct UnitObservation
{
    /// <summary>Whether the unit's target-point queue has emptied (move completion).</summary>
    public bool TargetPointsEmpty;

    /// <summary>Current health of the unit's assigned target.</summary>
    public float? TargetHealth;

    /// <summary>Whether the unit's assigned target has been destroyed (attack completion).</summary>
    public bool TargetDestroyed;

    /// <summary>Whether the structure the unit is building has finished construction.</summary>
    public bool BuildComplete;

    /// <summary>Whether a full supply gather/dump cycle has returned to its start state.</summary>
    public bool SupplyCycleComplete;

    /// <summary>Whether the unit's special-power ability effect has been observed.</summary>
    public bool SpecialPowerEffectObserved;
}

/// <summary>
/// Measures each unit's dispatch-to-completion duration in logic frames, with one completion
/// predicate per <see cref="PrimaryActionKind"/> and a per-unit frame budget that produces a
/// timeout distinct from a baseline regression. No direct <c>IGame</c>/<c>Scene3D</c> reference;
/// observations arrive through <see cref="UnitObservation"/>. See deep dive P9 §2.5.
/// </summary>
public sealed class UnitActionTimer
{
    private readonly List<TrackedUnit> _tracked = [];
    private readonly List<UnitGap> _gaps = [];

    /// <summary>Records that a unit's primary-action order was dispatched on <paramref name="frame"/>.</summary>
    public void OnDispatched(
        uint unitKey,
        string definitionName,
        int factionIndex,
        PrimaryAction action,
        uint frame,
        uint? targetKey = null)
    {
        _tracked.Add(new TrackedUnit
        {
            UnitKey = unitKey,
            DefinitionName = definitionName,
            FactionIndex = factionIndex,
            Action = action,
            StartFrame = frame,
            TargetKey = targetKey,
            FrameBudget = action.FrameBudget,
        });
    }

    /// <summary>Records a named gap for a unit that could not be measured.</summary>
    public void RecordGap(string definitionName, int factionIndex, PrimaryActionKind kind, string reason)
    {
        _gaps.Add(new UnitGap(definitionName, factionIndex, kind, reason));
    }

    /// <summary>
    /// Advances the timer to <paramref name="frame"/>, querying each still-pending unit's
    /// observation through <paramref name="observe"/>. Records a completion on the first matching
    /// signal, or a timeout when the unit's frame budget is exceeded.
    /// </summary>
    public void Poll(uint frame, Func<uint, UnitObservation> observe)
    {
        foreach (var unit in _tracked)
        {
            if (unit.Completed || unit.TimedOut)
            {
                continue;
            }

            var elapsed = frame - unit.StartFrame;
            if (elapsed >= (uint)unit.FrameBudget)
            {
                unit.TimedOut = true;
                continue;
            }

            var observation = observe(unit.UnitKey);
            if (IsComplete(unit, observation))
            {
                unit.Completed = true;
                unit.MeasuredFrames = elapsed;
            }
        }
    }

    /// <summary>Builds the immutable report from the recorded timings, gaps, and timeouts.</summary>
    public ValidationGroundReport BuildReport()
    {
        var report = new ValidationGroundReport();

        foreach (var unit in _tracked)
        {
            if (unit.Completed)
            {
                report.Timings.Add(new UnitTiming(
                    unit.DefinitionName,
                    unit.FactionIndex,
                    unit.Action.Kind,
                    unit.MeasuredFrames!.Value));
            }
            else if (unit.TimedOut)
            {
                report.Timeouts.Add(new UnitTimeout(
                    unit.DefinitionName,
                    unit.FactionIndex,
                    unit.Action.Kind,
                    unit.FrameBudget));
            }
        }

        report.Gaps.AddRange(_gaps);
        return report;
    }

    private static bool IsComplete(TrackedUnit unit, UnitObservation observation)
    {
        // Attack completes when the assigned target is destroyed: a unit has finished its attack
        // command only once the target is gone, not on the first damaging shot.
        if (unit.Action.Kind == PrimaryActionKind.Attack)
        {
            return observation.TargetDestroyed;
        }

        return unit.Action.Kind switch
        {
            PrimaryActionKind.Move => observation.TargetPointsEmpty,
            PrimaryActionKind.Build => observation.BuildComplete,
            PrimaryActionKind.Gather => observation.SupplyCycleComplete,
            PrimaryActionKind.SpecialPower => observation.SpecialPowerEffectObserved,
            _ => false,
        };
    }

    private sealed class TrackedUnit
    {
        public uint UnitKey;
        public string DefinitionName = "";
        public int FactionIndex;
        public PrimaryAction Action = null!;
        public uint StartFrame;
        public uint? TargetKey;
        public int FrameBudget;
        public bool Completed;
        public bool TimedOut;
        public uint? MeasuredFrames;
    }
}
