using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Panopticon.Data.Map;
using Panopticon.Graphics.Cameras;
using Panopticon.Gui;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Terrain;

namespace Panopticon.Navigation;

public class Navigation
{
    private readonly Graph _graph;
    private readonly HeightMap _heightMap;
    private readonly PathStatisticsAccumulator _statistics = new();
    private long _pathSearchTicks;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // Monotonic process-wide count of CalculatePath entries (WI-M1-02): polled by callers
    // that measure path-request load per tick; never reset.
    private static long TotalPathRequests;

    public static long PathRequestCount => Interlocked.Read(ref TotalPathRequests);

    public Navigation(BlendTileData tileData, HeightMap heightMap)
    {
        var width = tileData.Impassability.GetLength(0);
        var height = tileData.Impassability.GetLength(1);
        _graph = new Graph(width, height);

        for (var x = 0; x < _graph.Width; x++)
        {
            for (var y = 0; y < _graph.Height; y++)
            {
                var impassable = tileData.Impassability[x, y];
                _graph.GetNode(x, y).Passability = impassable ? Passability.Impassable : Passability.Passable;
            }
        }

        _heightMap = heightMap;
    }

    /// <summary>Behavior knobs consulted by group movement (WI-N2-01).</summary>
    public NavigationOptions Options { get; } = new();

    internal Graph Graph => _graph;

    // WI-N2-01: landing searches need graph-level access (closest cell, region labels) without
    // going through a full path request.
    internal Node GetNodeAt(Vector3 position) => GetClosestNode(_graph, position);

    internal Vector3 GetNodeWorldPosition(Node node)
    {
        var pos = GetNodePosition(node);
        return new Vector3(pos.X, pos.Y, _heightMap.GetHeight(pos.X, pos.Y));
    }

    public Vector2 GetNodePosition(Node node)
    {
        var xyz = _heightMap.GetPosition(node.X, node.Y);
        return new Vector2(xyz.X, xyz.Y);
    }

    private Node GetClosestNode(Graph graph, Vector3 pos)
    {
        var coords = _heightMap.GetTilePosition(pos);

        if (coords == null)
        {
            return null;
        }

        var (x, y) = coords.Value;
        // The graph can be smaller than the heightmap (border bands), so in-range heightmap
        // coordinates can still fall outside the graph.
        return graph.TryGetNode(x, y, out var node) ? node : null;
    }

    // Off-graph positions (formation offsets can push a destination past the map edge) are
    // impassable, not a crash: callers treat them as blocked and fall back to their own paths.
    public bool IsPassable(Vector3 position) => GetClosestNode(_graph, position)?.IsPassable == true;

    private Node GetNextPassablePosition(Graph graph, Vector3 start, Vector3 end)
    {
        var offset = end - start;
        var direction = Vector3.Normalize(offset);
        var currentNode = GetClosestNode(graph, start);

        var maxSteps = (int)offset.Vector2XY().Length() + 1;

        // The walk can step off the graph near borders; a null node stops the search and the
        // caller treats the position as unreachable.
        while (currentNode is { IsPassable: false } && maxSteps > 0)
        {
            start += direction;
            currentNode = GetClosestNode(graph, start);
            maxSteps--;
        }

        return maxSteps > 0 ? currentNode : null;
    }

    public IList<Vector3> CalculatePath(Vector3 start, Vector3 end, out bool endIsPassable, PathRequestOrigin origin = PathRequestOrigin.Unit)
    {
        Interlocked.Increment(ref TotalPathRequests);
        _statistics.AddRequest(origin);
        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        // The returned list is the single per-search allocation (WI-N1-01).
        var result = new List<Vector3>();
        ComputePath(_graph, start, end, out endIsPassable, result);
        _pathSearchTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp;
        return result;
    }

    /// <summary>
    /// Logic-thread ComputePath time accumulated since the last consume (WI-N0-01), emitted by
    /// the stepper as the nested path span. Async worker search time is excluded: it accumulates
    /// into <see cref="PathStatistics.WorkerSearchMs"/> instead.
    /// </summary>
    public TimeSpan ConsumePathSpan()
    {
        var ticks = Interlocked.Exchange(ref _pathSearchTicks, 0);
        return TimeSpan.FromSeconds(ticks / (double)System.Diagnostics.Stopwatch.Frequency);
    }

    /// <summary>Point-in-time snapshot of the navigation statistics (WI-N0-01).</summary>
    public PathStatistics Statistics => _statistics.Snapshot();

    /// <summary>Search-grid width in cells; reported by pathfinding-load scenarios.</summary>
    public int GridWidth => _graph.Width;

    /// <summary>Search-grid height in cells; reported by pathfinding-load scenarios.</summary>
    public int GridHeight => _graph.Height;

    // Shared search core (WI-B4-05): the synchronous CalculatePath and the async path worker
    // both run this against a graph. Workers pass a submission-time snapshot because
    // UpdateAreaPassability mutates the live graph on the logic thread. WI-N1-01: callers
    // supply the waypoint buffer; the core itself is allocation-free.
    internal void ComputePath(Graph graph, Vector3 start, Vector3 end, out bool endIsPassable, List<Vector3> result)
    {
        result.Clear();
        endIsPassable = true;
        var startNode = GetClosestNode(graph, start);
        var endNode = GetClosestNode(graph, end);

        // Off-graph endpoints (border-band tiles, formation offsets past the map edge) have no
        // path; callers already treat an empty path as no route.
        if (startNode == null || endNode == null)
        {
            Logger.Info(() => "Aborting pathfinding because start and/or end are outside the graph");
            _statistics.AddFailedSearch();
            return;
        }

        if (!startNode.IsPassable)
        {
            _statistics.AddEndpointAdjustment();
            startNode = GetNextPassablePosition(graph, start, end);
        }

        if (!endNode.IsPassable)
        {
            _statistics.AddEndpointAdjustment();
            endNode = GetNextPassablePosition(graph, end, start);
            endIsPassable = false;
        }

        if (startNode == null || endNode == null)
        {
            Logger.Info(() => "Aborting pathfinding because start and/or end are null");
            _statistics.AddFailedSearch();
            return;
        }

        // WI-N1-02: different connected regions can never yield a route; reject without the
        // grid-flooding search. Snapshot graphs carry their own labels, frozen at clone time.
        if (!graph.Regions.SameRegion(startNode, endNode))
        {
            Logger.Warn(() => $"Graph search failed to find a path between {start} and {end}.");
            _statistics.AddFailedSearch();
            return;
        }

        var nodesExpandedBefore = graph.SearchNodesExpanded;
        var route = graph.Search(startNode, endNode);
        _statistics.AddNodesExpanded(graph.SearchNodesExpanded - nodesExpandedBefore);

        if (route == null)
        {
            Logger.Warn(() => $"Graph search failed to find a path between {start} and {end}.");
            _statistics.AddFailedSearch();
            return;
        }

        PathOptimizer.RemoveRedundantNodes(route);
        PathOptimizer.SmoothPath(route, graph);

        foreach (var node in route)
        {
            var pos = GetNodePosition(node);
            result.Add(new Vector3(pos.X, pos.Y, _heightMap.GetHeight(pos.X, pos.Y)));
        }
    }

    public void UpdateAreaPassability(GameObject gameObject, bool passable)
    {
        foreach (var collider in gameObject.Colliders)
        {
            UpdateAreaPassability(collider, passable);
        }
    }

    public void UpdateAreaPassability(Collider collider, bool passable)
    {
        var axisAlignedBoundingArea = collider.AxisAlignedBoundingArea;

        var bottomLeft = new Vector3(axisAlignedBoundingArea.X, axisAlignedBoundingArea.Y, 0);
        var bottomLeftNode = GetClosestNode(_graph, bottomLeft);
        var topRight = new Vector3(axisAlignedBoundingArea.X + axisAlignedBoundingArea.Width, axisAlignedBoundingArea.Y + axisAlignedBoundingArea.Height, 0);
        var topRightNode = GetClosestNode(_graph, topRight);

        //sometimes map objects are places outside the actual map....
        if (bottomLeftNode == null || topRightNode == null)
        {
            return;
        }

        for (var x = bottomLeftNode.X; x < topRightNode.X; x++)
        {
            for (var y = bottomLeftNode.Y; y < topRightNode.Y; y++)
            {
                var node = _graph.GetNode(x, y);
                var position = GetNodePosition(node);
                var terrainHeight = _heightMap.GetHeight(position.X, position.Y);
                if (collider.Transform.Translation.Z <= terrainHeight &&
                    terrainHeight <= collider.Transform.Translation.Z + collider.Height &&
                    collider.Contains(position)) // TODO: should we use a rectangle here instead of only a point?
                {
                    node.Passability = passable ? Passability.Passable : Passability.Impassable;
                }
            }
        }

        // WI-N1-02: region labels must not outlive the passability change they describe.
        _graph.MarkRegionsDirty();

        // WI-N1-03: async submissions reuse their snapshot until the version changes, so every
        // passability mutation must bump it.
        _passabilityVersion++;
    }

    // WI-N1-03: monotonic passability version; async path submissions snapshot cells together
    // with this number and the worker only re-applies bytes when it changes.
    private int _passabilityVersion = 1;

    internal int PassabilityVersion => _passabilityVersion;

    internal PassabilitySnapshot BuildPassabilitySnapshot()
    {
        var cells = new byte[_graph.Width * _graph.Height];

        for (var x = 0; x < _graph.Width; x++)
        {
            for (var y = 0; y < _graph.Height; y++)
            {
                cells[x + (y * _graph.Width)] = (byte)(_graph.GetNode(x, y).IsPassable ? 1 : 0);
            }
        }

        return new PassabilitySnapshot(_passabilityVersion, cells);
    }

    private PathRequestQueue _pathRequestQueue;

    /// <summary>
    /// Frames an async path request may remain unresolved before counting as a deadline miss
    /// (WI-B4-05). Consulted when the queue is first created; set before the first submit.
    /// </summary>
    public int AsyncPathFrameBound { get; set; } = PathRequestQueue.DefaultFrameBound;

    /// <summary>
    /// Queues a path search computed off the logic thread against a submission-time passability
    /// snapshot (WI-B4-05). Completion applies on the logic thread via
    /// <see cref="DrainPathCompletions"/> in submission order. Does not increment
    /// <see cref="PathRequestCount"/>; that counter stays synchronous-request-only.
    /// </summary>
    public PathRequestHandle SubmitPathRequest(Vector3 start, Vector3 end, GameObject requester = null, Action<PathRequestHandle> onCompleted = null)
    {
        _pathRequestQueue ??= new PathRequestQueue(this, AsyncPathFrameBound);
        return _pathRequestQueue.Submit(start, end, requester, onCompleted);
    }

    /// <summary>Applies completed async path requests; called once per logic tick by the stepper.</summary>
    public void DrainPathCompletions() => _pathRequestQueue?.Drain();

    /// <summary>
    /// Deadline misses across async path requests (WI-B4-05): completions that applied with a
    /// wait longer than <see cref="AsyncPathFrameBound"/> frames. Zero before the first
    /// submission.
    /// </summary>
    public long AsyncPathDeadlineMisses => _pathRequestQueue?.DeadlineMisses ?? 0;

    /// <summary>Async path requests submitted but not yet terminal; zero before the first submission.</summary>
    public long AsyncPathOutstanding => _pathRequestQueue?.Outstanding ?? 0;

    /// <summary>WI-N4-02 run report: total async submissions; zero before the first submission.</summary>
    public long AsyncPathSubmitted => _pathRequestQueue?.SubmittedCount ?? 0;

    /// <summary>WI-N4-02 run report: completions applied at drain; zero before the first submission.</summary>
    public long AsyncPathApplied => _pathRequestQueue?.AppliedCount ?? 0;

    /// <summary>WI-N4-02 run report: submissions cancelled before applying; zero before the first submission.</summary>
    public long AsyncPathCancelled => _pathRequestQueue?.CancelledCount ?? 0;

    /// <summary>WI-N4-02 run report: waited-frames sample per applied request, in apply order.</summary>
    public int[] CopyAsyncPathWaitFrameSamples() => _pathRequestQueue?.CopyWaitFrameSamples() ?? [];

    internal Graph CloneGraphForWorker() => _graph.ClonePassability();

    // WI-N0-01: the async worker reports its search volume and time; the logic-thread span
    // (ConsumePathSpan) excludes worker time by design.
    internal void AddAsyncSearchMeasurement(long stopwatchTicks)
    {
        _statistics.AddRequest(PathRequestOrigin.Async);
        _statistics.AddWorkerSearchTicks(stopwatchTicks);
    }

    // WI-N2-01: follower-landing outcomes land in the statistics snapshot beside the request
    // counts they replace.
    internal void AddLandedFollower() => _statistics.AddLandedFollower();

    internal void AddFallbackFollower() => _statistics.AddFallbackFollower();

    public void DebugDraw(DrawingContext2D context, Camera camera)
    {
        _graph.DebugDraw(context, camera, GetNodePosition, _heightMap.GetHeight);
    }
}
