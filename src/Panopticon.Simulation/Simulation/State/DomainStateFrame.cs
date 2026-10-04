#nullable enable

using System.Collections.Generic;

namespace Panopticon.Simulation;

/// <summary>
/// The complete state of one domain at one frame. Records are ordinal by <see cref="DomainStateRecord.Key"/>
/// for stable diffs and reconstruction regardless of domain iteration order. Persistence arrives
/// with WI-A4-43.
/// </summary>
public sealed class DomainStateFrame
{
    public uint Frame { get; set; }

    public List<DomainStateRecord> Records { get; } = new();
}
