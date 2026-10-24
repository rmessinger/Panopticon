using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
    Graph _graph;
    List<(Node Start, Node End)> _cases;

    public int CaseCount => _cases.Count;

    [GlobalSetup]
    public void Setup()
    {
        var fixturePath = Path.Combine(
            FindRepositoryRoot(),
            "test",
            "Panopticon.Simulation.Tests",
            "Navigation",
            "PathEqualityCorpus.json");

        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = document.RootElement;
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        var bits = Convert.FromBase64String(root.GetProperty("passabilityBits").GetString()!);

        _graph = new Graph(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                _graph.GetNode(x, y).Passability = (bits[index / 8] & (1 << (index % 8))) != 0
                    ? Passability.Impassable
                    : Passability.Passable;
            }
        }

        _cases = [];
        foreach (var element in root.GetProperty("cases").EnumerateArray())
        {
            _cases.Add(
                (
                    _graph.GetNode(element.GetProperty("startX").GetInt32(), element.GetProperty("startY").GetInt32()),
                    _graph.GetNode(element.GetProperty("endX").GetInt32(), element.GetProperty("endY").GetInt32())
                ));
        }
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

    // BenchmarkDotNet executes from BenchmarkDotNet.Artifacts inside the repository, and a
    // worktree's .git is a file, not a directory, so both forms are probed.
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Repository root not found above {AppContext.BaseDirectory}.");
    }
}
