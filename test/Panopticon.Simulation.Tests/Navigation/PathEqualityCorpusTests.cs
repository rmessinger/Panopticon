using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Data.Map;
using Panopticon.Navigation;
using Panopticon.Tests.Data;
using Xunit;
using SageGame = Panopticon.SageGame;

namespace Panopticon.Tests.Navigation;

/// <summary>
/// WI-N1-01: the waypoint-equality corpus. The install-free test replays every fixture case
/// through Search plus the optimizer pipeline ComputePath uses, and asserts the exact node
/// sequence. A mismatch means search behavior drifted; per the card guardrail the fixture is
/// never regenerated to make the test pass. Deliberate regeneration (map update, sanctioned
/// behavior change) runs the Interactive test with PANOPTICON_PATH_CORPUS_OUTPUT set to the
/// fixture path, then commits the new fixture alongside the change.
/// </summary>
public class PathEqualityCorpusTests
{
    private static string FixturePath =>
        Path.Combine(Environment.CurrentDirectory, "Navigation", "PathEqualityCorpus.json");

    [Fact]
    public void Search_ReproducesEveryCorpusWaypointList()
    {
        var fixture = PathEqualityCorpus.Load(FixturePath);
        var graph = PathEqualityCorpus.BuildGraph(fixture);

        var previousSmoothing = PathOptimizer.EnablePathSmoothing;
        PathOptimizer.EnablePathSmoothing = true;

        try
        {
            foreach (var pathCase in fixture.Cases)
            {
                var startNode = graph.GetNode(pathCase.StartX, pathCase.StartY);
                var endNode = graph.GetNode(pathCase.EndX, pathCase.EndY);
                var route = graph.Search(startNode, endNode);

                if (!pathCase.Reachable)
                {
                    Assert.Null(route);

                    if (startNode.IsPassable && endNode.IsPassable)
                    {
                        Assert.False(
                            graph.Regions.SameRegion(startNode, endNode),
                            $"{pathCase.Name}: unreachable case must not share a region.");
                    }

                    continue;
                }

                Assert.NotNull(route);
                PathOptimizer.RemoveRedundantNodes(route);
                PathOptimizer.SmoothPath(route, graph);

                // WI-N1-02: for passable endpoints the region labels must agree with the
                // search outcome exactly; a disagreement is the deep-dive's top risk.
                // Impassable endpoints are skipped: ComputePath adjusts them before the check.
                if (startNode.IsPassable && endNode.IsPassable)
                {
                    Assert.True(
                        graph.Regions.SameRegion(startNode, endNode),
                        $"{pathCase.Name}: reachable case must share a region.");
                }

                Assert.True(
                    route.Count == pathCase.Waypoints.Count,
                    $"{pathCase.Name}: expected {pathCase.Waypoints.Count} waypoints, got {route.Count}.");

                for (var i = 0; i < route.Count; i++)
                {
                    Assert.True(
                        route[i].X == pathCase.Waypoints[i][0] && route[i].Y == pathCase.Waypoints[i][1],
                        $"{pathCase.Name}: waypoint {i} expected ({pathCase.Waypoints[i][0]}, {pathCase.Waypoints[i][1]}), got ({route[i].X}, {route[i].Y}).");
                }
            }
        }
        finally
        {
            PathOptimizer.EnablePathSmoothing = previousSmoothing;
        }
    }

    [Fact]
    public void Fixture_CoversAllCaseCategories()
    {
        var fixture = PathEqualityCorpus.Load(FixturePath);

        Assert.True(fixture.Cases.Count >= 30, $"Corpus has only {fixture.Cases.Count} cases.");
        Assert.True(
            fixture.CategoryCounts.GetValueOrDefault(PathEqualityCorpus.PassableCategory) >= 20,
            "Corpus must contain at least 20 passable cases.");
        Assert.True(
            fixture.CategoryCounts.GetValueOrDefault(PathEqualityCorpus.ImpassableCategory) >= 10,
            "Corpus must contain at least 10 impassable-endpoint cases.");
        Assert.True(
            fixture.CategoryCounts.GetValueOrDefault(PathEqualityCorpus.UnreachableCategory) >= 1,
            "Corpus must contain at least one unreachable case.");

        // Deepdivide's passable region is one connected component, so the corpus generator
        // splits it with synthetic structure walls (WI-N1-02); the map alone yields no
        // unreachable pairs. Diagonal-gap pairs remain map-dependent and stay covered by the
        // synthetic wall cases in GraphTest.

        var counted = 0;
        foreach (var count in fixture.CategoryCounts.Values)
        {
            counted += count;
        }

        Assert.Equal(fixture.Cases.Count, counted);
    }

    [GameFact(SageGame.CncGenerals)]
    [Trait("Category", "Interactive")]
    public void RegeneratedCorpus_MatchesTheCommittedFixture()
    {
        var graph = LoadSourceMapGrid();
        var generated = PathEqualityCorpus.Generate(graph, PathEqualityCorpus.SourceMapPath, PathEqualityCorpus.FixedSeed);
        var json = PathEqualityCorpus.Serialize(generated);

        var output = Environment.GetEnvironmentVariable("PANOPTICON_PATH_CORPUS_OUTPUT");
        if (output != null)
        {
            File.WriteAllText(output, json);
            return;
        }

        var committed = PathEqualityCorpus.Load(FixturePath);
        Assert.Equal(PathEqualityCorpus.Serialize(committed), json);
    }

    private static Graph LoadSourceMapGrid()
    {
        using var fileSystem = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals).CreateFileSystem();
        var entry = fileSystem.GetFile(PathEqualityCorpus.SourceMapPath);
        Assert.NotNull(entry);

        var mapFile = MapFile.FromFileSystemEntry(entry);
        Assert.NotNull(mapFile.BlendTileData);

        var impassability = mapFile.BlendTileData.Impassability;
        var graph = new Graph(impassability.GetLength(0), impassability.GetLength(1));

        for (var x = 0; x < graph.Width; x++)
        {
            for (var y = 0; y < graph.Height; y++)
            {
                graph.GetNode(x, y).Passability = impassability[x, y]
                    ? Passability.Impassable
                    : Passability.Passable;
            }
        }

        return graph;
    }
}
