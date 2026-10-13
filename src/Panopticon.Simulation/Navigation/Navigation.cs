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
        return graph.GetNode(x, y);
    }

    public bool IsPassable(Vector3 position) => GetClosestNode(_graph, position).IsPassable;

    private Node GetNextPassablePosition(Graph graph, Vector3 start, Vector3 end)
    {
        var offset = end - start;
        var direction = Vector3.Normalize(offset);
        var currentNode = GetClosestNode(graph, start);

        var maxSteps = (int)offset.Vector2XY().Length() + 1;

        while (!currentNode.IsPassable && maxSteps > 0)
        {
            start += direction;
            currentNode = GetClosestNode(graph, start);
            maxSteps--;
        }

        return maxSteps > 0 ? currentNode : null;
    }

    public IList<Vector3> CalculatePath(Vector3 start, Vector3 end, out bool endIsPassable)
    {
        Interlocked.Increment(ref TotalPathRequests);
        return ComputePath(_graph, start, end, out endIsPassable);
    }

    // Shared search core (WI-B4-05): the synchronous CalculatePath and the async path worker
    // both run this against a graph. Workers pass a submission-time snapshot because
    // UpdateAreaPassability mutates the live graph on the logic thread.
    internal IList<Vector3> ComputePath(Graph graph, Vector3 start, Vector3 end, out bool endIsPassable)
    {
        var result = new List<Vector3>();
        endIsPassable = true;
        var startNode = GetClosestNode(graph, start);
        var endNode = GetClosestNode(graph, end);

        if (!startNode.IsPassable)
        {
            startNode = GetNextPassablePosition(graph, start, end);
        }

        if (!endNode.IsPassable)
        {
            endNode = GetNextPassablePosition(graph, end, start);
            endIsPassable = false;
        }

        if (startNode == null || endNode == null)
        {
            Logger.Info("Aborting pathfinding because start and/or end are null");
            return result;
        }

        var route = graph.Search(startNode, endNode);

        if (route == null)
        {
            Logger.Warn($"Graph search failed to find a path between {start} and {end}.");
            return result;
        }

        PathOptimizer.RemoveRedundantNodes(route);
        PathOptimizer.SmoothPath(route, graph);

        foreach (var node in route)
        {
            var pos = GetNodePosition(node);
            result.Add(new Vector3(pos.X, pos.Y, _heightMap.GetHeight(pos.X, pos.Y)));
        }

        return result;
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
    }

    private PathRequestQueue? _pathRequestQueue;

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

    internal Graph CloneGraphForWorker() => _graph.ClonePassability();

    public void DebugDraw(DrawingContext2D context, Camera camera)
    {
        _graph.DebugDraw(context, camera, GetNodePosition, _heightMap.GetHeight);
    }
}
