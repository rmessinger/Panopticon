using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Terrain;

public sealed class TerrainLogic : IPersistableObject
{
    private readonly List<BridgeSurface> _bridgeSurfaces = [];

    private readonly List<BridgeCorridor> _bridgeCorridors = [];

    private int _activeBoundary = 0;

    public HeightMap HeightMap { get; private set; }

    /// <summary>
    /// Registers a generated-bridge roadway surface for the dedicated movement query
    /// (WP1029). Scene3D registers one surface per generated bridge after the map loads;
    /// the list is replaced on reload and cleared on dispose. Legacy bridges are never
    /// registered, so every other consumer keeps raw heightmap behavior.
    /// </summary>
    public void RegisterBridgeSurface(BridgeSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _bridgeSurfaces.Add(surface);
    }

    /// <summary>
    /// Registers a generated-bridge corridor for the tyre pose query. Scene3D registers
    /// one corridor per generated bridge after the map loads; the list is replaced on
    /// reload and cleared with the roadway surfaces.
    /// </summary>
    public void RegisterBridgeCorridor(BridgeCorridor corridor)
    {
        ArgumentNullException.ThrowIfNull(corridor);
        _bridgeCorridors.Add(corridor);
    }

    public void ClearBridgeSurfaces()
    {
        _bridgeSurfaces.Clear();
        _bridgeCorridors.Clear();
    }

    /// <summary>
    /// The tyre-pose weight for one vehicle's tyre contact points: the largest weight
    /// over the registered corridors, 0 when none is registered.
    /// </summary>
    internal float GetBridgeCorridorPoseWeight(ReadOnlySpan<Vector2> samples)
    {
        var weight = 0f;

        foreach (var corridor in _bridgeCorridors)
        {
            weight = MathF.Max(weight, corridor.PoseWeight(samples));
        }

        return weight;
    }

    /// <summary>True when at least one corridor is registered, so maps without generated-bridge corridors skip the client tyre pose work entirely.</summary>
    internal bool HasBridgeCorridors => _bridgeCorridors.Count > 0;

    /// <summary>
    /// Movement-only bridge roadway height at the XY point across every registered
    /// generated bridge, or null when no generated bridge roadway covers the point.
    /// Multi-level roadways (the Concrete model carries a upper roadway plus buried
    /// camber surfaces) resolve to the topmost level in this form.
    /// </summary>
    internal float? GetGeneratedBridgeSurfaceHeight(float x, float y)
    {
        float? topmost = null;

        foreach (var surface in _bridgeSurfaces)
        {
            if (surface.GetSurfaceZ(x, y) is { } z && (topmost is null || z > topmost.Value))
            {
                topmost = z;
            }
        }

        return topmost;
    }

    /// <summary>
    /// Same query resolved against a reference Z: among every roadway surface covering
    /// the XY point, returns the level closest to <paramref name="referenceZ"/>. The
    /// locomotor passes the vehicle's current Z so a multi-level roadway resolves to the
    /// level the vehicle is actually on, which the snap-margin gate then verifies.
    /// </summary>
    internal float? GetGeneratedBridgeSurfaceHeight(float x, float y, float referenceZ)
    {
        float? closest = null;
        var closestDistance = float.MaxValue;

        foreach (var surface in _bridgeSurfaces)
        {
            if (surface.GetSurfaceZ(x, y) is { } z)
            {
                var distance = MathF.Abs(z - referenceZ);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = z;
                }
            }
        }

        return closest;
    }

    /// <summary>
    /// True when the XY point is inside any registered generated bridge's roadway extent
    /// (extent only, regardless of whether a roadway triangle covers the exact point).
    /// </summary>
    internal bool IsWithinGeneratedBridgeExtent(float x, float y)
    {
        foreach (var surface in _bridgeSurfaces)
        {
            if (surface.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    public void SetHeightMapData(HeightMapData heightMapData)
    {
        HeightMap = new HeightMap(heightMapData);
    }

    // TODO(Port): Implement this.
    public bool IsCliffCell(float x, float y) => false;

    // TODO(Port): Implement this.
    public bool IsUnderwater(float x, float y)
    {
        return false;
    }

    // TODO(Port): Implement this.
    public bool IsUnderwater(float x, float y, out float waterZ)
    {
        waterZ = 0;
        return IsUnderwater(x, y);
    }

    public bool IsUnderwater(float x, float y, out float waterZ, out float terrainZ)
    {
        var result = IsUnderwater(x, y, out waterZ);

        terrainZ = GetGroundHeight(x, y);

        return result;
    }

    public float GetGroundHeight(float x, float y, out Vector3 normal)
    {
        normal = HeightMap.GetNormal(x, y);
        return HeightMap.GetHeight(x, y);
    }

    public float GetGroundHeight(float x, float y)
    {
        return HeightMap.GetHeight(x, y);
    }

    //TODO(Port): Implement actual logic.
    public void SetActiveBoundary(int newActiveBoundary)
    {
        _activeBoundary = newActiveBoundary;
    }

    public AxisAlignedBoundingBox GetExtent()
    {
        var maxXY = HeightMap.Boundaries.Length switch
        {
            0 => Vector2.Zero,
            _ => HeightMap.Boundaries[_activeBoundary].ToVector2() * HeightMap.HorizontalScale
        };

        var min = new Vector3(0, 0, HeightMap.MinZ);
        var max = new Vector3(maxXY.X, maxXY.Y, HeightMap.MaxZ);
        return new AxisAlignedBoundingBox(min, max);
    }

    // TODO(Port): Implement this.
    public float GetLayerHeight(float x, float y, PathfindLayerType layer, out Vector3 normal, bool clip = true)
    {
        normal = Vector3.UnitZ;
        return HeightMap.GetHeight(x, y);
    }

    // TODO(Port): Implement this.
    public float GetLayerHeight(float x, float y, PathfindLayerType layer)
    {
        return HeightMap.GetHeight(x, y);
    }

    // TODO(Port): Implement this.
    public PathfindLayerType GetLayerForDestination(in Vector3 pos) => PathfindLayerType.Ground;

    /// <summary>
    /// This is just like <see cref="GetLayerForDestination"/>, but always
    /// return the highest layer that will be <= z at that point (unlike
    /// <see cref="GetLayerForDestination"/>, which will return the closest
    /// layer).
    /// </summary>
    // TODO(Port): Implement this.
    public PathfindLayerType GetHighestLayerForDestination(in Vector3 pos, bool onlyHealthyBridges = false) => PathfindLayerType.Ground;

    public void Persist(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        reader.PersistVersion(2);
        reader.EndObject();

        reader.SkipUnknownBytes(8);
    }
}
