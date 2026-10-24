using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Panopticon.Navigation;

namespace Panopticon.Benchmarks.DataStructures;

/// <summary>
/// WI-N1-01: one invocation = one full pass over the committed path-equality corpus
/// fixture (deepdivide grid, passable routes at three distance buckets plus impassable
/// endpoints). Divide Mean and Allocated by <see cref="CaseCount"/> for per-search values.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15, invocationCount: 1)]
public class PathCorpusSearch
{
    Graph _graph = null!;
    List<(Node Start, Node End)> _cases = null!;

    public int CaseCount => _cases.Count;

    [GlobalSetup]
    public void Setup()
    {
        _graph = PathCorpusFixture.LoadGraph(out _cases);
    }

    [Benchmark]
    public Node CorpusPass()
    {
        Node last = null!;
        foreach (var (start, end) in _cases)
        {
            last = _graph.Search(start, end) is { Count: > 0 } route ? route[^1] : null!;
        }

        return last;
    }
}
