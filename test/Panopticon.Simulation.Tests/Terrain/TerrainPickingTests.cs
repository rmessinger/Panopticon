using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Simulation.Tests.Terrain;

public sealed class TerrainPickingTests
{
    private static HeightMap CreateHeightMap(int width, int height, uint borderWidth = 0, Func<int, int, ushort>? elevationFunc = null)
    {
        var elevations = new ushort[width + 1, height + 1];
        for (var y = 0; y <= height; y++)
        {
            for (var x = 0; x <= width; x++)
            {
                elevations[x, y] = elevationFunc != null
                    ? elevationFunc(x, y)
                    : (ushort)0;
            }
        }

        var data = HeightMapData.Create(borderWidth, elevations);
        return new HeightMap(data);
    }

    private static Vector3? BruteForceIntersect(HeightMap heightMap, Ray ray)
    {
        float? closestT = null;
        var numCellsX = heightMap.Width - 1;
        var numCellsY = heightMap.Height - 1;

        for (var y = 0; y < numCellsY; y++)
        {
            for (var x = 0; x < numCellsX; x++)
            {
                var p00 = heightMap.GetPosition(x, y);
                var p10 = heightMap.GetPosition(x + 1, y);
                var p01 = heightMap.GetPosition(x, y + 1);
                var p11 = heightMap.GetPosition(x + 1, y + 1);

                var tri1 = new Triangle(p00, p10, p01);
                var tri2 = new Triangle(p01, p10, p11);

                if (ray.Intersects(tri1, out var t1) && t1.HasValue)
                {
                    if (closestT == null || t1.Value < closestT.Value)
                    {
                        closestT = t1.Value;
                    }
                }

                if (ray.Intersects(tri2, out var t2) && t2.HasValue)
                {
                    if (closestT == null || t2.Value < closestT.Value)
                    {
                        closestT = t2.Value;
                    }
                }
            }
        }

        if (closestT == null)
        {
            return null;
        }

        return ray.Position + (ray.Direction * closestT.Value);
    }

    [Fact]
    public void VerticalRays_MatchAnalyticGroundHeight_FlatTerrain()
    {
        const ushort elevation = 80;
        var map = CreateHeightMap(17, 17, borderWidth: 0, (_, _) => elevation);
        var expectedHeight = elevation * map.VerticalScale;

        var testPoints = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(15f, 25f),
            new Vector2(73.4f, 48.9f),
            new Vector2(159.9f, 159.9f)
        };

        foreach (var point in testPoints)
        {
            var ray = new Ray(new Vector3(point.X, point.Y, 500f), -Vector3.UnitZ);
            var hit = TerrainHeightfieldRay.Intersect(map, ray);

            Assert.NotNull(hit);
            Assert.Equal(point.X, hit.Value.X, 3);
            Assert.Equal(point.Y, hit.Value.Y, 3);
            Assert.Equal(expectedHeight, hit.Value.Z, 3);
        }
    }

    [Fact]
    public void VerticalRays_MatchAnalyticGroundHeight_AffineSlope()
    {
        // On an affine slope Z = a*x + b*y + c, both triangles in every cell are coplanar
        var map = CreateHeightMap(33, 33, borderWidth: 1, (x, y) => (ushort)(50 + 2 * x + 3 * y));
        var minPos = map.GetPosition(0, 0);

        for (var sampleX = 10f; sampleX < 250f; sampleX += 37.3f)
        {
            for (var sampleY = 15f; sampleY < 250f; sampleY += 41.7f)
            {
                var cellX = (sampleX - minPos.X) / HeightMap.HorizontalScale;
                var cellY = (sampleY - minPos.Y) / HeightMap.HorizontalScale;
                var expectedZ = (50f + 2f * cellX + 3f * cellY) * map.VerticalScale;

                var ray = new Ray(new Vector3(sampleX, sampleY, 800f), -Vector3.UnitZ);
                var hit = TerrainHeightfieldRay.Intersect(map, ray);

                Assert.NotNull(hit);
                Assert.Equal(sampleX, hit.Value.X, 3);
                Assert.Equal(sampleY, hit.Value.Y, 3);
                Assert.True(
                    MathF.Abs(expectedZ - hit.Value.Z) < 1e-3f,
                    $"At ({sampleX}, {sampleY}): expected Z {expectedZ}, actual Z {hit.Value.Z}, diff {MathF.Abs(expectedZ - hit.Value.Z)}");
            }
        }
    }

    [Fact]
    public void OffCameraTargetHits_FunctionLevelIndependentOfGpuResidency()
    {
        // Large map (257x257). In CDLOD, far patches are non-resident.
        // Heightfield picking must evaluate source ground directly without GPU dependencies.
        var map = CreateHeightMap(257, 257, borderWidth: 0, (x, y) => (ushort)((x + y) * 2));

        var farTargetWorld = new Vector3(2345.6f, 2189.4f, 0f);
        var rayOrigin = new Vector3(farTargetWorld.X - 30f, farTargetWorld.Y - 40f, 600f);
        var rayDir = Vector3.Normalize(new Vector3(30f, 40f, -600f));
        var ray = new Ray(rayOrigin, rayDir);

        var hit = TerrainHeightfieldRay.Intersect(map, ray);
        Assert.NotNull(hit);

        // Verify the hit point lies on the terrain
        var minPos = map.GetPosition(0, 0);
        var cx = (int)MathF.Floor((hit.Value.X - minPos.X) / HeightMap.HorizontalScale);
        var cy = (int)MathF.Floor((hit.Value.Y - minPos.Y) / HeightMap.HorizontalScale);
        Assert.InRange(cx, 0, map.Width - 2);
        Assert.InRange(cy, 0, map.Height - 2);

        // Validate against local cell triangles
        var p00 = map.GetPosition(cx, cy);
        var p10 = map.GetPosition(cx + 1, cy);
        var p01 = map.GetPosition(cx, cy + 1);
        var p11 = map.GetPosition(cx + 1, cy + 1);
        var tri1 = new Triangle(p00, p10, p01);
        var tri2 = new Triangle(p01, p10, p11);

        var hitTri1 = ray.Intersects(tri1, out var t1);
        var hitTri2 = ray.Intersects(tri2, out var t2);
        Assert.True(hitTri1 || hitTri2);
    }

    [Fact]
    public void EdgeAndCornerPartialCells_HitCorrectly()
    {
        // Non-patch multiple size (20x25) with borderWidth = 2
        var map = CreateHeightMap(20, 25, borderWidth: 2, (x, y) => (ushort)(x * 5 + y * 7 + 20));
        var numCellsX = map.Width - 1;
        var numCellsY = map.Height - 1;

        var origin00 = map.GetPosition(0, 0);
        var minX = origin00.X;
        var minY = origin00.Y;
        var maxX = minX + numCellsX * HeightMap.HorizontalScale;
        var maxY = minY + numCellsY * HeightMap.HorizontalScale;

        var cornerAndEdgeRays = new List<Ray>
        {
            // Corners
            new(new Vector3(minX + 0.5f, minY + 0.5f, 400f), -Vector3.UnitZ),
            new(new Vector3(maxX - 0.5f, minY + 0.5f, 400f), -Vector3.UnitZ),
            new(new Vector3(minX + 0.5f, maxY - 0.5f, 400f), -Vector3.UnitZ),
            new(new Vector3(maxX - 0.5f, maxY - 0.5f, 400f), -Vector3.UnitZ),

            // Edges
            new(new Vector3(minX + 0.1f, minY + 50f, 400f), -Vector3.UnitZ),
            new(new Vector3(maxX - 0.1f, minY + 50f, 400f), -Vector3.UnitZ),
            new(new Vector3(minX + 50f, minY + 0.1f, 400f), -Vector3.UnitZ),
            new(new Vector3(minX + 50f, maxY - 0.1f, 400f), -Vector3.UnitZ),

            // Diagonal across map
            new(new Vector3(minX - 20f, minY - 20f, 500f), Vector3.Normalize(new Vector3(maxX - minX + 40f, maxY - minY + 40f, -400f))),
            new(new Vector3(maxX + 20f, maxY + 20f, 500f), Vector3.Normalize(new Vector3(-(maxX - minX + 40f), -(maxY - minY + 40f), -400f)))
        };

        foreach (var ray in cornerAndEdgeRays)
        {
            var expected = BruteForceIntersect(map, ray);
            var actual = TerrainHeightfieldRay.Intersect(map, ray);

            if (expected == null)
            {
                Assert.Null(actual);
            }
            else
            {
                Assert.NotNull(actual);
                Assert.True(
                    Vector3.Distance(expected.Value, actual.Value) < 1e-3f,
                    $"Ray {ray} mismatch: expected {expected.Value}, actual {actual.Value}");
            }
        }
    }

    [Fact]
    public void SlopedTerrain_ArbitraryAngleRays_MatchBruteForceScanWithinTolerance()
    {
        // 33x33 map with rolling hills
        var map = CreateHeightMap(33, 33, borderWidth: 1, (x, y) =>
            (ushort)(((x * 17) + (y * 31) + ((x ^ y) * 7)) % 300 + 50));

        var origin00 = map.GetPosition(0, 0);
        var minX = origin00.X;
        var minY = origin00.Y;
        var maxX = minX + (map.Width - 1) * HeightMap.HorizontalScale;
        var maxY = minY + (map.Height - 1) * HeightMap.HorizontalScale;

        var testRays = new List<Ray>
        {
            // Rays starting outside entering diagonally
            new(new Vector3(minX - 50f, minY + 80f, 400f), Vector3.Normalize(new Vector3(200f, 50f, -300f))),
            new(new Vector3(maxX + 60f, maxY - 40f, 350f), Vector3.Normalize(new Vector3(-180f, -70f, -250f))),
            new(new Vector3(minX + 120f, minY - 40f, 300f), Vector3.Normalize(new Vector3(40f, 150f, -220f))),
            new(new Vector3(minX + 90f, maxY + 50f, 450f), Vector3.Normalize(new Vector3(-30f, -160f, -380f))),

            // Rays starting inside traversing multiple cells
            new(new Vector3(minX + 25f, minY + 30f, 250f), Vector3.Normalize(new Vector3(100f, 120f, -150f))),
            new(new Vector3(minX + 150f, minY + 150f, 280f), Vector3.Normalize(new Vector3(-80f, -60f, -120f))),
            new(new Vector3(minX + 70f, minY + 180f, 260f), Vector3.Normalize(new Vector3(90f, -100f, -140f))),

            // Shallow angle rays
            new(new Vector3(minX + 10f, minY + 100f, 220f), Vector3.Normalize(new Vector3(150f, 20f, -40f))),
            new(new Vector3(maxX - 10f, minY + 120f, 220f), Vector3.Normalize(new Vector3(-140f, -10f, -35f))),

            // Rays crossing cell diagonals
            new(new Vector3(minX + 45f, minY + 45f, 300f), Vector3.Normalize(new Vector3(50f, 50f, -200f))),
            new(new Vector3(minX + 115f, minY + 85f, 300f), Vector3.Normalize(new Vector3(-50f, 50f, -200f)))
        };

        foreach (var ray in testRays)
        {
            var expected = BruteForceIntersect(map, ray);
            var actual = TerrainHeightfieldRay.Intersect(map, ray);

            if (expected == null)
            {
                Assert.Null(actual);
            }
            else
            {
                Assert.NotNull(actual);
                var distance = Vector3.Distance(expected.Value, actual.Value);
                Assert.True(
                    distance < 1e-3f,
                    $"Mismatch for ray {ray}: expected {expected.Value}, actual {actual.Value}, diff {distance}");
            }
        }
    }

    [Fact]
    public void RaysThatMiss_ReturnNull()
    {
        var map = CreateHeightMap(17, 17, borderWidth: 0, (_, _) => 50);

        var missingRays = new[]
        {
            // Pointing away vertically
            new Ray(new Vector3(50f, 50f, 200f), Vector3.UnitZ),

            // Parallel above terrain
            new Ray(new Vector3(-50f, 50f, 500f), Vector3.UnitX),

            // Parallel below terrain pointing down
            new Ray(new Vector3(50f, 50f, -100f), -Vector3.UnitZ),

            // Outside bounds pointing away horizontally
            new Ray(new Vector3(-50f, 50f, 100f), -Vector3.UnitX),
            new Ray(new Vector3(50f, -50f, 100f), -Vector3.UnitY),
            new Ray(new Vector3(250f, 50f, 100f), Vector3.UnitX),
            new Ray(new Vector3(50f, 250f, 100f), Vector3.UnitY),

            // Skew ray passing completely over terrain
            new Ray(new Vector3(-100f, -100f, 800f), Vector3.Normalize(new Vector3(1f, 1f, 0f)))
        };

        foreach (var ray in missingRays)
        {
            var hit = TerrainHeightfieldRay.Intersect(map, ray);
            Assert.Null(hit);
        }
    }

    [Fact]
    public void DegenerateRays_TerminateSafely()
    {
        var map = CreateHeightMap(17, 17, borderWidth: 0, (_, _) => 50);

        var degenerateRays = new[]
        {
            // Zero direction
            new Ray(new Vector3(50f, 50f, 200f), Vector3.Zero),

            // Near-zero direction
            new Ray(new Vector3(50f, 50f, 200f), new Vector3(1e-15f, 1e-15f, 1e-15f)),

            // NaN components
            new Ray(new Vector3(float.NaN, 50f, 200f), -Vector3.UnitZ),
            new Ray(new Vector3(50f, 50f, 200f), new Vector3(0f, float.NaN, -1f)),

            // Infinite components
            new Ray(new Vector3(float.PositiveInfinity, 50f, 200f), -Vector3.UnitZ),
            new Ray(new Vector3(50f, 50f, 200f), new Vector3(0f, 0f, float.NegativeInfinity)),

            // Parallel outside grid
            new Ray(new Vector3(-100f, 50f, 100f), Vector3.UnitY)
        };

        foreach (var ray in degenerateRays)
        {
            var hit = TerrainHeightfieldRay.Intersect(map, ray);
            Assert.Null(hit);
        }
    }
}
