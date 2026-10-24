using Panopticon.Navigation;
using Xunit;

namespace Panopticon.Tests.Navigation;

public class GraphTest
{
    [Fact]
    public void NoObstacles()
    {
        var graph = new Graph(100, 100);
        var start = graph.GetNode(10, 10);
        var end = graph.GetNode(90, 90);

        var result = graph.Search(start, end);
        Assert.Equal(80, result.Count);
    }

    [Fact]
    public void WithObstacles()
    {
        var graph = new Graph(100, 100);
        var start = graph.GetNode(10, 10);
        var end = graph.GetNode(90, 90);

        for (int i = 0; i < 99; i++)
        {
            graph.GetNode(50, i).Passability = Passability.Impassable;
        }

        var result = graph.Search(start, end);
        Assert.Equal(129, result.Count);
    }

    [Fact]
    public void Impassable()
    {
        var graph = new Graph(100, 100);
        var start = graph.GetNode(10, 10);
        var end = graph.GetNode(90, 90);

        for (int i = 0; i < 100; i++)
        {
            graph.GetNode(50, i).Passability = Passability.Impassable;
        }

        var result = graph.Search(start, end);
        Assert.Null(result);
    }

    // WI-N0-01: Search reports how many nodes it expanded so Navigation can attribute search
    // volume; a disconnected goal still floods its region, which is exactly the cost the
    // counter must make visible.
    [Fact]
    public void Search_CountsExpandedNodes()
    {
        var graph = new Graph(100, 100);
        Assert.Equal(0, graph.SearchNodesExpanded);

        var start = graph.GetNode(10, 10);
        var end = graph.GetNode(90, 90);

        graph.Search(start, end);
        var expandedAfterReachable = graph.SearchNodesExpanded;
        Assert.True(expandedAfterReachable > 0);

        for (int i = 0; i < 100; i++)
        {
            graph.GetNode(50, i).Passability = Passability.Impassable;
        }

        Assert.Null(graph.Search(start, end));
        Assert.True(graph.SearchNodesExpanded > expandedAfterReachable);
    }

    // WI-N1-01: the only passable crossing of the wall is reachable diagonally; the corpus
    // cannot produce this shape on deepdivide (single connected region), so it lives here.
    [Fact]
    public void Search_FindsRoutesThroughDiagonalOnlyGaps()
    {
        var graph = new Graph(100, 100);
        var start = graph.GetNode(10, 10);
        var end = graph.GetNode(90, 90);

        for (var y = 0; y < 100; y++)
        {
            graph.GetNode(50, y).Passability = Passability.Impassable;
        }

        graph.GetNode(50, 50).Passability = Passability.Passable;
        graph.GetNode(49, 50).Passability = Passability.Impassable;
        graph.GetNode(51, 50).Passability = Passability.Impassable;

        var result = graph.Search(start, end);
        Assert.NotNull(result);
        Assert.Contains(graph.GetNode(50, 50), result);
    }

    // WI-N1-01: Search fills a per-graph scratch list instead of allocating a route per call.
    [Fact]
    public void Search_ReusesTheRouteScratchList()
    {
        var graph = new Graph(100, 100);
        var start = graph.GetNode(5, 5);
        var end = graph.GetNode(50, 50);

        var first = graph.Search(start, end);
        var second = graph.Search(start, end);

        Assert.Same(first, second);
        Assert.Equal(45, second.Count);
    }
}
