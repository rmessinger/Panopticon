using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Panopticon.Navigation;

namespace Panopticon.Benchmarks.DataStructures;

/// <summary>
/// WI-N1-02: region label costs on the M0 (deepdivide) grid. Relabel and ColdRelabel
/// measure the labeling maintenance cost; UnreachableSearchFlood measures the search
/// cost an early region reject avoids per unreachable request.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15, invocationCount: 1)]
public class RegionRelabel
{
    Graph _graph = null!;
    NavigationRegions _regions = null!;
    Node _a = null!;
    Node _b = null!;
    Node _farA = null!;
    Node _farB = null!;

    [GlobalSetup]
    public void Setup()
    {
        _graph = PathCorpusFixture.LoadGraph(out var cases);
        _regions = new NavigationRegions(_graph);

        foreach (var (start, end) in cases)
        {
            if (!_regions.SameRegion(start, end)
                && start.Passability == Passability.Passable
                && end.Passability == Passability.Passable)
            {
                _farA = start;
                _farB = end;
                break;
            }
        }

        (_a, _b) = cases[0];
    }

    [Benchmark]
    public bool Relabel()
    {
        _regions.MarkDirty();
        return _regions.SameRegion(_a, _b);
    }

    [Benchmark]
    public bool ColdRelabel()
    {
        var regions = new NavigationRegions(_graph);
        return regions.SameRegion(_a, _b);
    }

    [Benchmark]
    public List<Node> UnreachableSearchFlood()
    {
        return _graph.Search(_farA, _farB);
    }
}
