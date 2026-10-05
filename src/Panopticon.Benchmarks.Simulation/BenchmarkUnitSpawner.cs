using System;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Direct-spawn helpers mirroring the TestHarness validation ground: definitions resolve
/// through the asset store, objects spawn through the real create path, and colliders refresh
/// immediately so placement is visible before the next logic tick.
/// </summary>
public static class BenchmarkUnitSpawner
{
    /// <summary>Spawns one unit at ground height; returns null for unknown definitions.</summary>
    public static GameObject? SpawnAt(Game game, string definitionName, Player owner, float x, float y, float yawRadians = 0f)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionName);
        ArgumentNullException.ThrowIfNull(owner);

        var definition = game.AssetStore.ObjectDefinitions.GetByName(definitionName);
        if (definition == null)
        {
            return null;
        }

        var height = game.Scene3D.Terrain.GetGroundHeight(new Vector2(x, y)) ?? 0f;
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawRadians);
        gameObject.UpdateTransform(new Vector3(x, y, height), rotation);
        gameObject.UpdateColliders();
        return gameObject;
    }

    /// <summary>Column-major grid position: index walks X first, then Y.</summary>
    public static (float X, float Y) GridPosition(int index, int columns, float x0, float y0, float spacing)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        return (x0 + (index % columns) * spacing, y0 + (index / columns) * spacing);
    }

    /// <summary>
    /// A spawn origin inside the map's border-excluded usable area, a given fraction of the
    /// way along each axis.
    /// </summary>
    public static (float X, float Y) OriginInUsableArea(Game game, float fractionX, float fractionY)
    {
        ArgumentNullException.ThrowIfNull(game);

        var heightMap = game.Scene3D.Terrain.HeightMap;
        return (heightMap.MaxXCoordinate * fractionX, heightMap.MaxYCoordinate * fractionY);
    }
}
