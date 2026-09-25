#nullable enable

using System.Collections.Generic;

namespace Panopticon.Simulation;

/// <summary>
/// Per-player metric rows for the T0 run-output section (RTS projection; non-player domains
/// leave this empty).
/// </summary>
public sealed record DomainPlayerMetrics(string Player, uint Money, int OwnedObjects);

/// <summary>One value in a named, keyed domain metric series.</summary>
public sealed record DomainMetricValue(string Series, string Key, double Value);

/// <summary>
/// The metrics snapshot a domain supplies on request: player rows plus domain-specific
/// series (transport: edge throughput, node occupancy).
/// </summary>
public sealed class DomainMetricsSnapshot
{
    public List<DomainPlayerMetrics> Players { get; } = new();

    public List<DomainMetricValue> Series { get; } = new();
}
