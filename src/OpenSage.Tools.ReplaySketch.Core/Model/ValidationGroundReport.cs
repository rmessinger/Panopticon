using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// Per-unit validation-ground results. Durations, gaps, and timeouts are three distinct
/// collections: a timeout means the action never completed within its budget; a gap means the
/// unit/action could not be measured (unimplemented handler, unresolved definition, or
/// spawn-versus-production misbehaviour). See deep dive P9 §2.5, §2.6.
/// </summary>
public sealed class ValidationGroundReport
{
    public List<UnitTiming> Timings { get; } = [];
    public List<UnitGap> Gaps { get; } = [];
    public List<UnitTimeout> Timeouts { get; } = [];

    public int TotalUnitCount => Timings.Count + Gaps.Count + Timeouts.Count;
}

/// <summary>A measured dispatch-to-completion duration for one unit, in logic frames.</summary>
public sealed record UnitTiming(
    string DefinitionName,
    int FactionIndex,
    PrimaryActionKind ActionKind,
    uint Frames);

/// <summary>A unit whose primary action could not be measured, with the reason.</summary>
public sealed record UnitGap(
    string DefinitionName,
    int FactionIndex,
    PrimaryActionKind ActionKind,
    string Reason);

/// <summary>A unit whose action did not complete within its frame budget.</summary>
public sealed record UnitTimeout(
    string DefinitionName,
    int FactionIndex,
    PrimaryActionKind ActionKind,
    int FrameBudget);
