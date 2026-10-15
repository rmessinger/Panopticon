using System;
using System.Collections.Generic;
using Panopticon.Recording;

namespace Panopticon.Simulation;

/// <summary>
/// A domain event drained into the operational stream (WI-A1-19): the kind identifies the event
/// family and the optional detail carries a compact payload.
/// </summary>
public sealed record DomainEvent(string Kind, string Detail = null);

/// <summary>
/// Reconstructable domain contract (WI-A1-19): a domain model that can drive operational
/// recording. Domains implement this to supply their cadence, the layers excluded from the
/// operational stream, boundary roll-ups, pending operational events, and runtime state outside
/// the domain-state schema.
/// </summary>
public interface IReconstructableDomain
{
    /// <summary>Boundary cadence this domain records at; overrides any caller default.</summary>
    RecordingCadence Cadence { get; }

    /// <summary>Layers excluded from the operational stream because roll-ups summarize them.</summary>
    IReadOnlySet<string> GranularLayers { get; }

    /// <summary>Captures roll-up records summarizing the granular layers at a boundary frame.</summary>
    IReadOnlyList<DomainStateRecord> CaptureRollups(uint frame);

    /// <summary>Drains and returns operational events accumulated since the last boundary.</summary>
    IReadOnlyList<DomainEvent> DrainOperationalEvents();

    /// <summary>Persists runtime state not covered by the domain-state schema (queues, counters).</summary>
    void PersistRuntime(StatePersister persister);
}
