#nullable enable

using System.Collections.Generic;

namespace OpenSage.Simulation;

/// <summary>
/// The metrics snapshot a domain supplies on request: player rows plus domain-specific
/// series (transport: edge throughput, node occupancy).
/// </summary>
public sealed class DomainMetricsSnapshot
{
    public List<DomainPlayerMetrics> Players { get; } = new();

    public List<DomainMetricValue> Series { get; } = new();
}
