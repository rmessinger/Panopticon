#nullable enable

using System;

namespace Panopticon.Simulation;

/// <summary>
/// One keyed domain state observation at one frame. <see cref="Integers"/>, <see cref="Reals"/>,
/// and <see cref="References"/> hold the kind's fields in declaration order; a record whose
/// vector lengths disagree with its kind is invalid and the recorder throws on capture.
/// Persistence arrives with WI-A4-43.
/// </summary>
public sealed class DomainStateRecord
{
    public string Key { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    /// <summary>The per-player (or per-owner) index the record belongs to.</summary>
    public int OwnerIndex { get; set; }

    public long[] Integers { get; set; } = Array.Empty<long>();

    public double[] Reals { get; set; } = Array.Empty<double>();

    /// <summary>Stable keys named by the kind's <see cref="DomainRecordKind.ReferenceNames"/>.</summary>
    public string[] References { get; set; } = Array.Empty<string>();
}
