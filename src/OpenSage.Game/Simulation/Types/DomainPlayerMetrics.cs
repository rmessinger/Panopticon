#nullable enable

namespace OpenSage.Simulation;

/// <summary>
/// Per-player metric rows for the T0 run-output section (RTS projection; non-player domains
/// leave this empty).
/// </summary>
public sealed record DomainPlayerMetrics(string Player, uint Money, int OwnedObjects);
