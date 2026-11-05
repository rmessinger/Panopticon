using System;
using System.Numerics;
using Panopticon.Geospatial.Grid;
using Panopticon.Terrain;

namespace Panopticon.Tests.Tools.GeoImport;

internal static class SedanProofHeightSampling
{
    // Continuous bilinear sample; the integer-node HeightMap.GetHeight(int, int) lookup ignores the fractional part.
    public static float? SampleContinuousEngineHeight(HeightMap heightMap, double engineX, double engineY)
    {
        ArgumentNullException.ThrowIfNull(heightMap);

        var worldPosition = new Vector2((float)engineX, (float)engineY);

        if (heightMap.GetTilePosition(worldPosition) is null)
        {
            return null;
        }

        return heightMap.GetHeight(worldPosition.X, worldPosition.Y);
    }

    public static (double AimEngineX, double AimEngineY) PlayableCentreEngineXy(int chunksX, int chunksY, double cropOriginGameX, double cropOriginGameY)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksY, 1);

        var payloadCentreGameX = 0.5d * chunksX * ChunkLayout.ChunkSpanGameUnits;
        var payloadCentreGameY = 0.5d * chunksY * ChunkLayout.ChunkSpanGameUnits;

        return (
            SedanProofBudgets.EngineXyFromPayload(payloadCentreGameX, cropOriginGameX),
            SedanProofBudgets.EngineXyFromPayload(payloadCentreGameY, cropOriginGameY));
    }
}
