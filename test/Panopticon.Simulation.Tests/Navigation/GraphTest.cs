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
}
