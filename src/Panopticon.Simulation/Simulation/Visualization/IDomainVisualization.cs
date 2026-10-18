#nullable enable

using System.Collections.Generic;

namespace Panopticon.Simulation;

/// <summary>
/// Consumer-facing visualization projection (WI-A5-12): what a domain offers a renderer. A
/// domain that also implements this interface exposes visual primitives addressed by the same
/// stable bucket keys as T1 recording, the metric series <see cref="ISimulationDomainModel.SampleMetrics"/>
/// emits, optional categorical overlays, and topology validation findings. Consumers
/// (ReplaySketch views, exports) program against this interface only and take no dependency on
/// a concrete domain adapter.
/// </summary>
public interface IDomainVisualization
{
    /// <summary>
    /// One snapshot of renderable primitives. Points carry the entity's stable bucket key so
    /// visualization coloring, T1 signature drill-down, and divergence reports line up.
    /// </summary>
    IReadOnlyList<DomainVisualPrimitive> CaptureVisualization();

    /// <summary>
    /// Descriptors for the series <see cref="ISimulationDomainModel.SampleMetrics"/> emits.
    /// Domains whose metrics ride the T0 player vector instead (RTS) return an empty list.
    /// </summary>
    IReadOnlyList<DomainMetricDescriptor> DescribeMetrics();

    /// <summary>
    /// Optional categorical overlays (owner, kind, fidelity, …). A domain with no overlay
    /// returns an empty list, never null: absence is explicit.
    /// </summary>
    IReadOnlyList<DomainOverlayDescriptor> DescribeOverlays();

    /// <summary>Topology validation findings; empty means the topology is consistent.</summary>
    IReadOnlyList<string> ValidateTopology();
}
