using System.Collections.Generic;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N8-01: stationary-unit footprints inside the local repath window. Collected over
/// the game's objects by the blocked-unit resolver, then applied to the window graph as
/// impassable cells so a detour routes around whatever is standing in the way. Moving units
/// are excluded: they vacate their cells on their own.
/// </summary>
internal sealed class LocalOccupancyWindow
{
    internal const float FootprintPadding = 2.5f;

    private readonly List<(Vector3 Position, float Radius)> _stationary = new();

    /// <summary>
    /// Collects the stationary ground-unit footprints that overlap the window's world bounds.
    /// The requester is excluded so its own start cell stays passable.
    /// </summary>
    public static LocalOccupancyWindow Collect(GameObject requester, float minX, float minY, float maxX, float maxY)
    {
        var window = new LocalOccupancyWindow();
        foreach (var obj in requester.GameEngine.GameLogic.Objects)
        {
            if (obj == requester || obj.IsEffectivelyDead || obj.IsStructure || obj.ContainedBy != null
                || obj.IsKindOf(ObjectKinds.Aircraft) || obj.IsKindOf(ObjectKinds.Immobile)
                || obj.IsKindOf(ObjectKinds.NoCollide))
            {
                continue;
            }

            var radius = obj.Geometry.BoundingCircleRadius + FootprintPadding;
            var position = obj.Translation;
            if (position.X < minX - radius || position.X > maxX + radius
                || position.Y < minY - radius || position.Y > maxY + radius)
            {
                continue;
            }

            if (IsAdvancing(obj))
            {
                continue;
            }

            window._stationary.Add((position, radius));
        }

        return window;
    }

    /// <summary>True while the object is actively advancing along a route of its own.</summary>
    public static bool IsAdvancing(GameObject obj)
    {
        return obj.AIUpdate is { TargetPoints.Count: > 0 } ai
            && obj.ModelConditionFlags.Get(ModelConditionFlag.Moving)
            && ai.Progress.FramesWithoutProgress == 0;
    }

    /// <summary>True when no collected footprint overlaps the position within the given radius.</summary>
    public bool IsFreeAt(Vector3 position, float radius)
    {
        foreach (var (otherPosition, otherRadius) in _stationary)
        {
            var combined = otherRadius + radius;
            if (Vector3.DistanceSquared(position, otherPosition) <= combined * combined)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Marks the collected footprints impassable on the window graph.</summary>
    public void Mark(Navigation navigation, Graph local, int offsetX, int offsetY)
    {
        foreach (var (position, radius) in _stationary)
        {
            var radiusSq = radius * radius;
            for (var y = 0; y < local.Height; y++)
            {
                for (var x = 0; x < local.Width; x++)
                {
                    var world = navigation.GetNodeWorldPosition(navigation.Graph.GetNode(x + offsetX, y + offsetY));
                    var dx = world.X - position.X;
                    var dy = world.Y - position.Y;
                    if (dx * dx + dy * dy <= radiusSq)
                    {
                        local.GetNode(x, y).Passability = Passability.Impassable;
                    }
                }
            }
        }
    }
}
