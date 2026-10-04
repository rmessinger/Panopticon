using System;
using System.Collections.Generic;
using Panopticon.Logic.Orders;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>Record kinds stored in operational stream chunks (WI-A1-20 §5.2).</summary>
internal enum OperationalRecordType : byte
{
    Tick = 1,
    Strings = 2,
    DomainDelta = 3,
    DomainSnapshot = 4,
    Rollups = 5,
    Metrics = 6,
    Events = 7,
    Inputs = 8,
    T1 = 10,
    WorldDelta = 11,
}

/// <summary>Base type for every record stored in an operational stream chunk.</summary>
internal abstract record OperationalRecord
{
    public abstract OperationalRecordType Type { get; }
}

/// <summary>Host frame, logic frame, and optional theater time at the boundary.</summary>
internal sealed record OperationalTick(uint HostFrame, uint LogicFrame, long? TheaterTimeTicks) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.Tick;
}

/// <summary>New entries of the append-only string table; emitted automatically by the writer.</summary>
internal sealed record OperationalStrings(IReadOnlyList<string> Entries) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.Strings;
}

/// <summary>A4-4 upserts and removals for non-granular layers since the previous boundary.</summary>
internal sealed record OperationalDomainDelta(uint BoundaryFrame, DomainStateDelta Delta) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.DomainDelta;
}

/// <summary>Full non-granular state; written at checkpoint boundaries only.</summary>
internal sealed record OperationalDomainSnapshot(uint BoundaryFrame, DomainStateFrame Frame) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.DomainSnapshot;
}

/// <summary>Rollup records captured at the boundary, encoded like snapshot records.</summary>
internal sealed record OperationalRollups(uint BoundaryFrame, IReadOnlyList<DomainStateRecord> Records) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.Rollups;
}

/// <summary>One T0 metric sample: series name, series key, value.</summary>
internal sealed record OperationalMetricSample(string Series, string Key, double Value);

/// <summary>T0 metric samples at the boundary.</summary>
internal sealed record OperationalMetrics(uint BoundaryFrame, IReadOnlyList<OperationalMetricSample> Samples) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.Metrics;
}

/// <summary>Domain events drained since the previous boundary.</summary>
internal sealed record OperationalEvents(uint BoundaryFrame, IReadOnlyList<DomainEvent> Events) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.Events;
}

/// <summary>One order dispatched since the previous boundary and the frame it dispatched at.</summary>
internal sealed record OperationalDispatchedOrder(uint InjectionFrame, uint DispatchFrame, Order Order);

/// <summary>Orders dispatched since the previous boundary.</summary>
internal sealed record OperationalInputs(IReadOnlyList<OperationalDispatchedOrder> Orders) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.Inputs;
}

/// <summary>Opaque tolerance-quantized signature-tree payload (A5-4).</summary>
internal sealed record OperationalT1(uint BoundaryFrame, IReadOnlyList<byte> Payload) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.T1;
}

/// <summary>
/// World-state summary changes since the previous boundary for domains without an
/// <see cref="IDomainStateProjection"/> (A1-6 §5.2: the summary is the state of record).
/// </summary>
internal sealed record OperationalWorldDelta(uint BoundaryFrame, WorldStateDelta Delta) : OperationalRecord
{
    public override OperationalRecordType Type => OperationalRecordType.WorldDelta;
}
