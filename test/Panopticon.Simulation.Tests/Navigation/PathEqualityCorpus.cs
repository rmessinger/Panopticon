using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Navigation;

namespace Panopticon.Tests.Navigation;

/// <summary>
/// WI-N1-01: seeded waypoint-equality corpus over the M0 (deepdivide) search grid. The fixture
/// stores the grid passability so the equality test runs install-free; only regeneration needs
/// the installed map. A corpus mismatch means engine drift, never a stale fixture: regeneration
/// is a deliberate operator action (the Interactive test with PANOPTICON_PATH_CORPUS_OUTPUT
/// set), never a way to make a failing equality test pass.
/// </summary>
public sealed record PathEqualityCase(
    string Name,
    string Category,
    int StartX,
    int StartY,
    int EndX,
    int EndY,
    bool Reachable,
    List<int[]> Waypoints);

public sealed record PathEqualityCorpusFixture(
    int SchemaVersion,
    string Map,
    int Seed,
    int Width,
    int Height,
    string PassabilityBits,
    Dictionary<string, int> CategoryCounts,
    List<PathEqualityCase> Cases);

public static class PathEqualityCorpus
{
    public const int FixedSeed = 1;
    public const string SourceMapPath = "maps/deepdivide/deepdivide.map";

    public const string PassableCategory = "passable";
    public const string ImpassableCategory = "impassable";
    public const string DiagonalGapCategory = "diagonal-gap";
    public const string UnreachableCategory = "unreachable";

    private const int PassableCasesPerBucket = 7;
    private const int ImpassableCasesPerKind = 5;
    private const int DiagonalGapCases = 10;
    private const int UnreachableCases = 10;
    private const int MaxSamplingAttempts = 20000;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize(PathEqualityCorpusFixture fixture)
    {
        return JsonSerializer.Serialize(fixture, JsonOptions);
    }

    public static PathEqualityCorpusFixture Load(string path)
    {
        return JsonSerializer.Deserialize<PathEqualityCorpusFixture>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Path equality corpus fixture at {path} is empty.");
    }

    public static Graph BuildGraph(PathEqualityCorpusFixture fixture)
    {
        var bits = Convert.FromBase64String(fixture.PassabilityBits);
        var graph = new Graph(fixture.Width, fixture.Height);

        for (var y = 0; y < fixture.Height; y++)
        {
            for (var x = 0; x < fixture.Width; x++)
            {
                var index = y * fixture.Width + x;
                var impassable = (bits[index / 8] & (1 << (index % 8))) != 0;
                graph.GetNode(x, y).Passability = impassable ? Passability.Impassable : Passability.Passable;
            }
        }

        return graph;
    }

    /// <summary>Builds the corpus from the given grid, mirroring the Navigation constructor's
    /// passability mapping. Sampling is fully deterministic for a fixed grid and seed.</summary>
    public static PathEqualityCorpusFixture Generate(Graph graph, string map, int seed)
    {
        var previousSmoothing = PathOptimizer.EnablePathSmoothing;
        PathOptimizer.EnablePathSmoothing = true;

        try
        {
            return GenerateCases(graph, map, seed);
        }
        finally
        {
            PathOptimizer.EnablePathSmoothing = previousSmoothing;
        }
    }

    private static PathEqualityCorpusFixture GenerateCases(Graph graph, string map, int seed)
    {
        var random = new Random(seed);
        var cases = new List<PathEqualityCase>();
        var passable = CollectCells(graph, passable: true);
        var impassable = CollectCells(graph, passable: false);

        GeneratePassableCases(graph, random, passable, cases);
        GenerateImpassableCases(graph, random, passable, impassable, cases);
        var labels8 = LabelCells(graph, eightConnected: true);
        var labels4 = LabelCells(graph, eightConnected: false);
        GenerateDiagonalGapCases(graph, passable, labels8, labels4, cases);
        GenerateUnreachableCases(graph, passable, labels8, cases);

        var counts = new Dictionary<string, int>();
        foreach (var pathCase in cases)
        {
            counts.TryGetValue(pathCase.Category, out var count);
            counts[pathCase.Category] = count + 1;
        }

        return new PathEqualityCorpusFixture(
            1,
            map,
            seed,
            graph.Width,
            graph.Height,
            PackPassability(graph),
            counts,
            cases);
    }

    // Distance buckets keep short, medium, and long searches in the corpus so the equality and
    // benchmark passes cover the cost range the N0 baseline measured.
    private static void GeneratePassableCases(Graph graph, Random random, List<(int X, int Y)> passable, List<PathEqualityCase> cases)
    {
        (int Min, int Max)[] buckets = { (1, 31), (32, 160), (161, int.MaxValue) };
        var bucketNames = new[] { "short", "medium", "long" };

        for (var bucket = 0; bucket < buckets.Length; bucket++)
        {
            var found = 0;
            for (var attempt = 0; attempt < MaxSamplingAttempts && found < PassableCasesPerBucket; attempt++)
            {
                var start = passable[random.Next(passable.Count)];
                var end = passable[random.Next(passable.Count)];
                var distance = Math.Max(Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y));

                if (distance < buckets[bucket].Min || distance > buckets[bucket].Max)
                {
                    continue;
                }

                var route = SearchAndOptimize(graph, start, end);
                if (route == null)
                {
                    continue;
                }

                found++;
                cases.Add(ToCase($"passable-{bucketNames[bucket]}-{found:00}", PassableCategory, start, end, route));
            }
        }
    }

    // Impassable endpoints pin the raw Search contract (Navigation adjusts endpoints before
    // searching); end-impassable goals are unreachable through the neighbour rule, while
    // start-impassable cells can still expand outward into a route.
    private static void GenerateImpassableCases(
        Graph graph,
        Random random,
        List<(int X, int Y)> passable,
        List<(int X, int Y)> impassable,
        List<PathEqualityCase> cases)
    {
        for (var i = 0; i < ImpassableCasesPerKind; i++)
        {
            var start = impassable[random.Next(impassable.Count)];
            var end = passable[random.Next(passable.Count)];
            var route = SearchAndOptimize(graph, start, end);
            cases.Add(ToCase($"impassable-start-{i:00}", ImpassableCategory, start, end, route));
        }

        for (var i = 0; i < ImpassableCasesPerKind; i++)
        {
            var start = passable[random.Next(passable.Count)];
            var end = impassable[random.Next(impassable.Count)];
            var route = SearchAndOptimize(graph, start, end);
            cases.Add(ToCase($"impassable-end-{i:00}", ImpassableCategory, start, end, route));
        }
    }

    // Diagonal-gap pairs connect in 8 neighbours but not in 4: the only route squeezes through
    // diagonal corners. This is the case region labels (WI-N1-02) must never reject.
    private static void GenerateDiagonalGapCases(
        Graph graph,
        List<(int X, int Y)> passable,
        int[] labels8,
        int[] labels4,
        List<PathEqualityCase> cases)
    {
        var width = graph.Width;
        var byComponent = new Dictionary<int, List<(int X, int Y)>>();

        foreach (var cell in passable)
        {
            var label = labels8[cell.Y * width + cell.X];
            if (!byComponent.TryGetValue(label, out var cells))
            {
                cells = new List<(int X, int Y)>();
                byComponent[label] = cells;
            }

            cells.Add(cell);
        }

        var found = 0;
        foreach (var component in byComponent.Values)
        {
            if (found >= DiagonalGapCases)
            {
                break;
            }

            var firstByLabel = new Dictionary<int, (int X, int Y)>();
            foreach (var cell in component)
            {
                var label = labels4[cell.Y * width + cell.X];
                firstByLabel.TryAdd(label, cell);
            }

            if (firstByLabel.Count < 2)
            {
                continue;
            }

            var anchor = firstByLabel.Values.First();
            foreach (var other in firstByLabel.Values.Skip(1))
            {
                if (found >= DiagonalGapCases)
                {
                    break;
                }

                var route = SearchAndOptimize(graph, anchor, other);
                if (route == null)
                {
                    continue;
                }

                found++;
                cases.Add(ToCase($"diagonal-gap-{found:00}", DiagonalGapCategory, anchor, other, route));
            }
        }
    }

    private static void GenerateUnreachableCases(Graph graph, List<(int X, int Y)> passable, int[] labels8, List<PathEqualityCase> cases)
    {
        var width = graph.Width;
        var byLabel = new Dictionary<int, List<(int X, int Y)>>();

        foreach (var cell in passable)
        {
            var label = labels8[cell.Y * width + cell.X];
            if (!byLabel.TryGetValue(label, out var cells))
            {
                cells = new List<(int X, int Y)>();
                byLabel[label] = cells;
            }

            cells.Add(cell);
        }

        // Deterministic pairing across components: the largest component against each other,
        // so the corpus always contains real region-flood failures when the map has any.
        var ordered = byLabel.Values.OrderByDescending(cells => cells.Count).ToList();
        if (ordered.Count < 2)
        {
            return;
        }

        var anchor = ordered[0][0];
        var found = 0;
        foreach (var component in ordered.Skip(1))
        {
            if (found >= UnreachableCases)
            {
                break;
            }

            found++;
            var end = component[0];
            cases.Add(new PathEqualityCase(
                $"unreachable-{found:00}",
                UnreachableCategory,
                anchor.X,
                anchor.Y,
                end.X,
                end.Y,
                Reachable: false,
                Waypoints: null));
        }
    }

    private static List<Node> SearchAndOptimize(Graph graph, (int X, int Y) start, (int X, int Y) end)
    {
        var route = graph.Search(graph.GetNode(start.X, start.Y), graph.GetNode(end.X, end.Y));
        if (route == null)
        {
            return null;
        }

        // The corpus stores the exact route ComputePath produces: optimized and smoothed.
        PathOptimizer.RemoveRedundantNodes(route);
        PathOptimizer.SmoothPath(route, graph);
        return route;
    }

    private static PathEqualityCase ToCase(string name, string category, (int X, int Y) start, (int X, int Y) end, List<Node> route)
    {
        if (route == null)
        {
            return new PathEqualityCase(name, category, start.X, start.Y, end.X, end.Y, Reachable: false, Waypoints: null);
        }

        var waypoints = new List<int[]>(route.Count);
        foreach (var node in route)
        {
            waypoints.Add([node.X, node.Y]);
        }

        return new PathEqualityCase(name, category, start.X, start.Y, end.X, end.Y, Reachable: true, waypoints);
    }

    private static List<(int X, int Y)> CollectCells(Graph graph, bool passable)
    {
        var cells = new List<(int X, int Y)>(graph.Width * graph.Height / 2);

        for (var y = 0; y < graph.Height; y++)
        {
            for (var x = 0; x < graph.Width; x++)
            {
                if (graph.GetNode(x, y).IsPassable == passable)
                {
                    cells.Add((x, y));
                }
            }
        }

        return cells;
    }

    private static int[] LabelCells(Graph graph, bool eightConnected)
    {
        var labels = new int[graph.Width * graph.Height];
        var queue = new Queue<int>();
        var nextLabel = 0;

        for (var y = 0; y < graph.Height; y++)
        {
            for (var x = 0; x < graph.Width; x++)
            {
                var index = y * graph.Width + x;
                if (labels[index] != 0 || !graph.GetNode(x, y).IsPassable)
                {
                    continue;
                }

                nextLabel++;
                labels[index] = nextLabel;
                queue.Enqueue(index);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var currentX = current % graph.Width;
                    var currentY = current / graph.Width;
                    var neighbours = eightConnected
                        ? new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) }
                        : new[] { (0, -1), (-1, 0), (1, 0), (0, 1) };

                    foreach (var (dx, dy) in neighbours)
                    {
                        if (!graph.TryGetNode(currentX + dx, currentY + dy, out var node))
                        {
                            continue;
                        }

                        var neighbourIndex = (currentY + dy) * graph.Width + currentX + dx;
                        if (labels[neighbourIndex] == 0 && node.IsPassable)
                        {
                            labels[neighbourIndex] = nextLabel;
                            queue.Enqueue(neighbourIndex);
                        }
                    }
                }
            }
        }

        return labels;
    }

    private static string PackPassability(Graph graph)
    {
        var bits = new byte[(graph.Width * graph.Height + 7) / 8];

        for (var y = 0; y < graph.Height; y++)
        {
            for (var x = 0; x < graph.Width; x++)
            {
                if (!graph.GetNode(x, y).IsPassable)
                {
                    var index = y * graph.Width + x;
                    bits[index / 8] |= (byte)(1 << (index % 8));
                }
            }
        }

        return Convert.ToBase64String(bits);
    }
}
