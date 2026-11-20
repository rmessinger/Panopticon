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

    private int _activeBoundary = 0;

    public HeightMap HeightMap { get; private set; }

    /// <summary>
    /// Registers a bridge roadway surface for ground-height queries (WP1029). Scene3D
    /// registers one surface per bridge after the map loads; the list is replaced on
    /// reload and cleared on dispose.
    /// </summary>
    public void RegisterBridgeSurface(BridgeSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _bridgeSurfaces.Add(surface);
    }

    public void ClearBridgeSurfaces()
    {
        _bridgeSurfaces.Clear();
    }

    /// <summary>
    /// Highest roadway surface Z at the XY point across every registered bridge, or null
    /// when no bridge roadway covers the point.
    /// </summary>
    public float? GetBridgeSurfaceZ(float x, float y)
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

        if (GetBridgeSurfaceZ(x, y) is { } bridgeZ)
        {
            return bridgeZ;
        }

        return HeightMap.GetHeight(x, y);
    }

    public float GetGroundHeight(float x, float y)
    {
        if (GetBridgeSurfaceZ(x, y) is { } bridgeZ)
        {
            return bridgeZ;
        }

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
        normal = HeightMap.GetNormal(x, y);

        if (GetBridgeSurfaceZ(x, y) is { } bridgeZ)
        {
            return bridgeZ;
        }

        return HeightMap.GetHeight(x, y);
    }

    // TODO(Port): Implement this.
    public float GetLayerHeight(float x, float y, PathfindLayerType layer)
    {
        if (GetBridgeSurfaceZ(x, y) is { } bridgeZ)
        {
            return bridgeZ;
        }

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
