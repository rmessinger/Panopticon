using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Panopticon.Navigation;

namespace Panopticon.Benchmarks.DataStructures;

/// <summary>
/// Loads the committed path-equality corpus fixture (deepdivide grid plus the two
/// generator structure walls) into a <see cref="Graph"/> for benchmark setup.
/// </summary>
internal static class PathCorpusFixture
{
    public static Graph LoadGraph(out List<(Node Start, Node End)> cases)
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

        var graph = new Graph(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                graph.GetNode(x, y).Passability = (bits[index / 8] & (1 << (index % 8))) != 0
                    ? Passability.Impassable
                    : Passability.Passable;
            }
        }

        cases = [];
        foreach (var element in root.GetProperty("cases").EnumerateArray())
        {
            cases.Add(
                (
                    graph.GetNode(element.GetProperty("startX").GetInt32(), element.GetProperty("startY").GetInt32()),
                    graph.GetNode(element.GetProperty("endX").GetInt32(), element.GetProperty("endY").GetInt32())
                ));
        }

        return graph;
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
