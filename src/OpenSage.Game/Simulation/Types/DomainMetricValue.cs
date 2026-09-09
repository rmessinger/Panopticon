#nullable enable

namespace OpenSage.Simulation;

/// <summary>One value in a named, keyed domain metric series.</summary>
public sealed record DomainMetricValue(string Series, string Key, double Value);
