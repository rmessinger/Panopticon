using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

// WI-A5-12: the visualization consumer contract is domain-agnostic. This test consumes
// IDomainVisualization and the primitive records from the ReplaySketch side without touching
// any concrete domain adapter, proving the Core-facing surface carries no RTS dependency.

public class DomainVisualizationConsumptionTests
{
    private sealed class StubDomain : IDomainVisualization
    {
        public IReadOnlyList<DomainVisualPrimitive> CaptureVisualization() =>
        [
            new DomainVisualPoint("zone:a", new Vector3(1, 1, 0), 0, "worker"),
            new DomainVisualPoint("zone:a", new Vector3(2, 2, 0), 1, "worker"),
            new DomainVisualPoint("zone:b", new Vector3(9, 9, 0), 0, "depot"),
            new DomainVisualPolyline("zone:a", [new Vector3(1, 1, 0), new Vector3(9, 9, 0)], "route"),
        ];

        public IReadOnlyList<DomainMetricDescriptor> DescribeMetrics() =>
            [new DomainMetricDescriptor("flow", null, "Flow")];

        public IReadOnlyList<DomainOverlayDescriptor> DescribeOverlays() =>
            [new DomainOverlayDescriptor("zone", "Zone", ["zone:a", "zone:b"])];

        public IReadOnlyList<string> ValidateTopology() => ["zone:c is unreachable"];
    }

    [Fact]
    public void Consumer_GroupsPrimitivesByBucketKey_WithoutDomainKnowledge()
    {
        IDomainVisualization domain = new StubDomain();

        var byBucket = domain.CaptureVisualization()
            .GroupBy(p => p.BucketKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        Assert.Equal(2, byBucket.Count);
        Assert.Equal(3, byBucket["zone:a"].Count);
        Assert.Single(byBucket["zone:b"]);
    }

    [Fact]
    public void Consumer_ReadsOverlayCategoriesAndTopologyFindings()
    {
        IDomainVisualization domain = new StubDomain();

        var overlay = Assert.Single(domain.DescribeOverlays());
        Assert.Equal(["zone:a", "zone:b"], overlay.Categories);
        var issue = Assert.Single(domain.ValidateTopology());
        Assert.Contains("zone:c", issue);
    }
}
