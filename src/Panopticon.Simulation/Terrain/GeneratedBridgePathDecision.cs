#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.Terrain;

/// <summary>
/// The path chosen for one marked generated bridge, planned against the pre-change heights before anything in
/// the world is created: either the level deck with its corridor plan, or the authored placement with the
/// converter's landing band applied at runtime, which reproduces a package the converter graded. A bridge that
/// cannot take the level path never meets a partly planned world, because its decision already holds the fallback.
/// </summary>
internal sealed record GeneratedBridgePathDecision
{
    public required Bridge Bridge { get; init; }

    public required bool TakesLevelPath { get; init; }

    public GeneratedBridgeLayout? Layout { get; init; }

    public GeneratedBridgeCorridorPlan? Corridor { get; init; }

    public IReadOnlyList<BridgeLandingBand.Target> LandingBand { get; init; } = [];

    /// <summary>Why the level path was not taken; null on the level path.</summary>
    public required string? Reason { get; init; }

    public static bool IsSupportedAsset(string? typeName, string? geometryFingerprint)
    {
        return string.Equals(typeName, BridgeSurface.SupportedTemplateName, StringComparison.OrdinalIgnoreCase)
            && BridgeSurface.IsSupportedGeometry(geometryFingerprint);
    }

    /// <summary>
    /// Border width of the height map in lattice samples: the playable lattice starts at this index.
    /// </summary>
    internal static int Border(HeightMap heightMap)
    {
        ArgumentNullException.ThrowIfNull(heightMap);

        return (heightMap.Width - (heightMap.MaxXCoordinate / HeightMap.HorizontalScale)) / 2;
    }

    public static GeneratedBridgePathDecision Decide(Bridge bridge, HeightMap heightMap)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(heightMap);

        // A marked package can never be left with ungraded landings: an off-grid map has no band to plan.
        if (Math.Abs(heightMap.VerticalScale - BridgeLandingBand.ElevationStep) > 1e-9)
        {
            throw new InvalidDataException(
                $"generated bridge path plan needs the 16-bit height grid (vertical scale {heightMap.VerticalScale} differs from {BridgeLandingBand.ElevationStep}).");
        }

        if (bridge.MapObject is null || !GeneratedBridgePathHandoff.IsMarked(bridge.MapObject))
        {
            return Fallback(bridge, heightMap, "handoff version is not implemented");
        }

        if (!IsSupportedAsset(bridge.TypeName, bridge.GeometryFingerprint))
        {
            return Fallback(bridge, heightMap, "unsupported asset or geometry");
        }

        if (!Bridge.GeneratedPathPlanEnabled)
        {
            return Fallback(bridge, heightMap, "level path disabled");
        }

        var start = bridge.StartPoint;
        var end = bridge.EndPoint;
        var datum = GeneratedBridgeCorridorPlanner.Datum(heightMap, start, end);
        var layout = bridge.TryBuildGeneratedLayout(start, end, datum, out var layoutReason);

        if (layout is null)
        {
            return Fallback(bridge, heightMap, "level layout unsupported: " + layoutReason);
        }

        var corridor = GeneratedBridgeCorridorPlanner.TryPlan(heightMap, layout, out var corridorReason);

        if (corridor is null)
        {
            return Fallback(bridge, heightMap, "corridor unsupported: " + corridorReason);
        }

        return new GeneratedBridgePathDecision
        {
            Bridge = bridge,
            TakesLevelPath = true,
            Layout = layout,
            Corridor = corridor,
            Reason = null,
        };
    }

    private static GeneratedBridgePathDecision Fallback(Bridge bridge, HeightMap heightMap, string reason)
    {
        return new GeneratedBridgePathDecision
        {
            Bridge = bridge,
            TakesLevelPath = false,
            LandingBand = PlanLandingBand(bridge, heightMap),
            Reason = reason,
        };
    }

    /// <summary>
    /// The converter's landing band for this bridge line, planned over the same lattice the converter plans over:
    /// the stored height-map data is one column and one row wider than <see cref="HeightMap.Width"/> (the
    /// constructor trims the last, unrendered column), so the lattice point counts are Width + 1 - 2 * border.
    /// Planning one short would leave the band off the map's far edge entirely.
    /// </summary>
    private static IReadOnlyList<BridgeLandingBand.Target> PlanLandingBand(Bridge bridge, HeightMap heightMap)
    {
        var border = Border(heightMap);
        var width = heightMap.Width + 1 - (2 * border);
        var height = heightMap.Height + 1 - (2 * border);
        var start = bridge.StartPoint;
        var end = bridge.EndPoint;

        return BridgeLandingBand.Plan(
            (x, y) => heightMap.GetHeight(x + border, y + border),
            width,
            height,
            HeightMap.HorizontalScale,
            [((double)start.X, (double)start.Y, (double)end.X, (double)end.Y)]);
    }
}
