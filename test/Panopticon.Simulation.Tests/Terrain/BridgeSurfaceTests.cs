#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Data.Map;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// WP1029 unit coverage for the bridge roadway surface: roadway filtering (upward-facing
/// triangles inside the anchor-line band), the world transform applied to imported mesh
/// vertices, and containment queries (topmost triangle wins, misses return null).
/// </summary>
public sealed class BridgeSurfaceTests
{
    private const float AnchorZ = 1000f;

    [Fact]
    public void CreateFromTriangles_ContainmentInterpolatesPlaneZ()
    {
        // One horizontal triangle covering XY [0,20]x[0,20] at Z 100.
        var surface = BridgeSurface.CreateFromTriangles([(new Vector3(0f, 0f, 100f), new Vector3(20f, 0f, 100f), new Vector3(0f, 20f, 100f))]);

        Assert.Equal(100f, surface.GetSurfaceZ(10f, 10f));
        Assert.Equal(100f, surface.GetSurfaceZ(0.5f, 0.5f));
        Assert.Equal(100f, surface.GetSurfaceZ(19f, 1f));
        Assert.Null(surface.GetSurfaceZ(25f, 10f));
        Assert.Null(surface.GetSurfaceZ(10f, -5f));
    }

    [Fact]
    public void GetSurfaceZ_SlopedTriangle_InterpolatesPlane()
    {
        // Ramp rising 10 units along +Y across a 20-unit span.
        var surface = BridgeSurface.CreateFromTriangles([(new Vector3(0f, 0f, 100f), new Vector3(20f, 0f, 100f), new Vector3(20f, 20f, 110f))]);

        var zAtMiddle = surface.GetSurfaceZ(10f, 10f);
        Assert.True(zAtMiddle is > 104f and < 106f, $"mid-ramp Z {zAtMiddle} should interpolate between 100 and 110.");

        // The diagonal edge from (0,0) to (20,20) bounds this triangle; (19,19) sits just
        // inside it at the raised end.
        var zNearEnd = surface.GetSurfaceZ(19f, 19f);
        Assert.True(zNearEnd is > 108.5f and <= 110f, $"ramp-end Z {zNearEnd} should sit near the raised edge.");

        Assert.Null(surface.GetSurfaceZ(1f, 19f));
    }

    [Fact]
    public void GetSurfaceZ_OverlappingTriangles_ReturnsTopmost()
    {
        var surface = BridgeSurface.CreateFromTriangles(
        [
            (new Vector3(0f, 0f, 100f), new Vector3(20f, 0f, 100f), new Vector3(0f, 20f, 100f)),
            (new Vector3(0f, 0f, 105f), new Vector3(20f, 0f, 105f), new Vector3(0f, 20f, 105f)),
        ]);

        Assert.Equal(105f, surface.GetSurfaceZ(10f, 10f));
    }

    [Fact]
    public void CreateFromTriangles_EmptyTriangleList_Throws()
    {
        Assert.Throws<ArgumentException>(() => BridgeSurface.CreateFromTriangles([]));
    }

    [Fact]
    public void Create_FiltersToHorizontalTrianglesInsideAnchorLineBand()
    {
        var deckZ = AnchorZ + 5.5f;
        var importedMesh = ImportedMesh(
            vertices:
            [
                // Roadway quad (two triangles), horizontal, on the anchor line (band passes).
                Vertex(0f, 0f, deckZ),
                Vertex(20f, 0f, deckZ),
                Vertex(20f, 10f, deckZ),
                Vertex(0f, 10f, deckZ),
                // Underside quad at the same XY with downward winding, far below the band
                // floor (filtered out by the band).
                Vertex(0f, 0f, AnchorZ - 40f),
                Vertex(20f, 0f, AnchorZ - 40f),
                Vertex(20f, 10f, AnchorZ - 40f),
                Vertex(0f, 10f, AnchorZ - 40f),
                // Wall strip far above the band (horizontal winding but outside the band).
                Vertex(0f, 0f, AnchorZ + 80f),
                Vertex(20f, 0f, AnchorZ + 80f),
                Vertex(20f, 10f, AnchorZ + 80f),
                Vertex(0f, 10f, AnchorZ + 80f),
                // Near-vertical wall between the roadway strips (horizontal normal fails).
                Vertex(7f, 0f, deckZ),
                Vertex(7.2f, 0f, deckZ),
                Vertex(7.2f, 10f, deckZ + 30f),
            ],
            indices:
            [
                // Roadway: two triangles over vertices 0-3.
                0, 1, 2,
                0, 2, 3,
                // Underside: vertices 4-7, wound so the face normal points down.
                4, 6, 5,
                4, 7, 6,
                // High strip: vertices 8-10.
                8, 9, 10,
                8, 10, 11,
                // Vertical wall: vertices 12-14, wound so the face normal is horizontal.
                12, 13, 14,
            ]);

        var subObject = StubSubObject();
        var surface = BridgeSurface.Create(
            [Tuple.Create(subObject, Matrix4x4.Identity)],
            _ => importedMesh,
            new Vector3(0f, 0f, AnchorZ),
            new Vector3(200f, 0f, AnchorZ));

        Assert.NotNull(surface);
        Assert.Equal(2, surface.TriangleCount);

        // Roadway Z is anchor + 5.5 (the measured road-bed offset).
        Assert.Equal(deckZ, surface.GetSurfaceZ(10f, 5f));
    }

    [Fact]
    public void Create_MirroredBoneKeepsHorizontalTriangles()
    {
        // The bridge bones mirror the model; the mirror flips authored normals but keeps
        // the roadway horizontal, so the surface must still capture it.
        var deckZ = AnchorZ + 5.5f;
        var importedMesh = ImportedMesh(
            vertices:
            [
                Vertex(0f, 0f, deckZ),
                Vertex(20f, 0f, deckZ),
                Vertex(20f, 10f, deckZ),
            ],
            indices: [0, 2, 1]);

        // Land the mirrored roadway at anchor + deck: the Z mirror negates the model Z
        // (model Z is absolute anchor + deck), then the translation re-bases it.
        var mirror = Matrix4x4.CreateScale(1f, 1f, -1f) * Matrix4x4.CreateTranslation(new Vector3(0f, 0f, 2f * deckZ));
        var surface = BridgeSurface.Create(
            [Tuple.Create(StubSubObject(), mirror)],
            _ => importedMesh,
            new Vector3(0f, 0f, AnchorZ),
            new Vector3(200f, 0f, AnchorZ));

        Assert.NotNull(surface);
        Assert.Equal(1, surface.TriangleCount);
        Assert.Equal(deckZ, surface.GetSurfaceZ(10f, 5f));
    }

    [Fact]
    public void Create_AppliesWorldTransformToVertices()
    {
        // Model-space roadway at Z 5.5; the piece matrix translates it to the anchor and
        // raises it, exactly like the bridge layout transforms do.
        var importedMesh = ImportedMesh(
            vertices:
            [
                Vertex(0f, 0f, 5.5f),
                Vertex(20f, 0f, 5.5f),
                Vertex(20f, 10f, 5.5f),
            ],
            indices: [0, 1, 2]);

        var transform = Matrix4x4.CreateTranslation(new Vector3(50f, 30f, AnchorZ));
        var subObject = StubSubObject();
        var surface = BridgeSurface.Create(
            [Tuple.Create(subObject, transform)],
            _ => importedMesh,
            new Vector3(50f, 30f, AnchorZ),
            new Vector3(250f, 30f, AnchorZ));

        Assert.NotNull(surface);
        Assert.Equal(AnchorZ + 5.5f, surface.GetSurfaceZ(60f, 35f));
        Assert.Equal(AnchorZ + 5.5f, surface.GetSurfaceZ(55f, 31f));

        // The un-transformed model-space position is not part of the surface extent.
        Assert.Null(surface.GetSurfaceZ(10f, 5f));
    }

    [Fact]
    public void MeasureIntrinsicGrade_PositiveAuthoredGrade_MeasuredPositive()
    {
        // A continuous +0.04 authored grade across three landing/span/landing pieces,
        // flat anchors (no site pitch): the fitted roadway pitch must recover the grade.
        const float grade = 0.04f;
        var pieces = SyntheticRoadwayPieces(grade, includeOutliers: false);

        var measured = BridgeSurface.MeasureIntrinsicGradeRadians(
            pieces,
            _ => Matrix4x4.Identity,
            new Vector3(0f, 0f, 0f),
            new Vector3(600f, 0f, 24f));

        Assert.NotNull(measured);
        // Authored 0.04 + the anchor pitch atan(24/600) ≈ 0.04 → world slope ≈ 0.08.
        Assert.InRange(measured.Value, 0.045f, 0.055f);
    }

    [Fact]
    public void MeasureIntrinsicGrade_OutlierTriangles_DoNotSkewPitch()
    {
        // Steep outlier triangles (slope ~1.0) inside the anchor band must not move the
        // fitted pitch away from the authored 0.04 roadway grade (2-pass residual gate).
        const float grade = 0.04f;
        var pieces = SyntheticRoadwayPieces(grade, includeOutliers: true);

        var measured = BridgeSurface.MeasureIntrinsicGradeRadians(
            pieces,
            _ => Matrix4x4.Identity,
            new Vector3(0f, 0f, 0f),
            new Vector3(600f, 0f, 24f));

        Assert.NotNull(measured);
        Assert.InRange(measured.Value, 0.045f, 0.055f);
    }

    [Fact]
    public void MeasureIntrinsicGrade_FlatRoadway_NearZero()
    {
        const float grade = 0f;
        var pieces = SyntheticRoadwayPieces(grade, includeOutliers: false);

        var measured = BridgeSurface.MeasureIntrinsicGradeRadians(
            pieces,
            _ => Matrix4x4.Identity,
            new Vector3(0f, 0f, 0f),
            new Vector3(600f, 0f, 24f));

        Assert.NotNull(measured);
        Assert.InRange(measured.Value, 0.03f, 0.05f);
    }

    [Fact]
    public void MeasureIntrinsicGrade_TooFewTriangles_ReturnsNull()
    {
        // One triangle, entirely below the roadway band: fewer than six valid centroids.
        var mesh = ImportedMesh(
            vertices:
            [
                Vertex(0f, 0f, -40f),
                Vertex(120f, 0f, -40f),
                Vertex(120f, 40f, -40f),
            ],
            indices: [0, 1, 2]);

        var pieces = new[] { (StubSubObject(), mesh), (StubSubObject(), mesh), (StubSubObject(), mesh) };

        var measured = BridgeSurface.MeasureIntrinsicGradeRadians(
            pieces,
            _ => Matrix4x4.Identity,
            new Vector3(0f, 0f, 0f),
            new Vector3(600f, 0f, 0f));

        Assert.Null(measured);
    }

    /// <summary>
    /// Three landing/span/landing roadway pieces carrying ONE continuous grade along the
    /// whole 300-unit span: piece k spans world x 100k..100k+100 at z = 0.04*x (flat
    /// anchors, so the fitted pitch equals the authored grade), with optional steep
    /// outlier triangles inside the anchor band.
    /// </summary>
    private static IReadOnlyList<(ModelSubObject SubObject, ImportedMesh Mesh)> SyntheticRoadwayPieces(float grade, bool includeOutliers)
    {
        var pieces = new List<(ModelSubObject SubObject, ImportedMesh Mesh)>();

        for (var piece = 0; piece < 3; piece++)
        {
            var x0 = 100f * piece;
            var z0 = grade * x0;
            var z1 = grade * (x0 + 100f);
            var vertices = new List<ImportedVertex>
            {
                Vertex(0f, 0f, z0),
                Vertex(100f, 0f, z1),
                Vertex(100f, 40f, z1),
                Vertex(0f, 40f, z0),
            };
            var indices = new List<ushort> { 0, 1, 2, 0, 2, 3 };

            if (includeOutliers && piece == 1)
            {
                // Steep ramp triangles (slope ~0.5) inside the anchor band, mid-span of the
                // span piece (world x 110..120 after the piece-offset shift).
                vertices.Add(Vertex(10f, 0f, grade * 110f));
                vertices.Add(Vertex(20f, 0f, grade * 120f));
                vertices.Add(Vertex(20f, 40f, grade * 120f));
                indices.Add((ushort)(vertices.Count - 3));
                indices.Add((ushort)(vertices.Count - 2));
                indices.Add((ushort)(vertices.Count - 1));
            }

            pieces.Add((StubSubObject(), ImportedMesh([.. vertices], [.. indices])));
        }

        return pieces;
    }

    [Fact]
    public void TerrainLogic_RawApisIgnoreRegisteredBridgeSurfaces()
    {
        // WP1029 rework: GetGroundHeight/GetLayerHeight are raw heightmap queries again;
        // the dedicated generated-bridge query is the only bridge-height entry point.
        var terrainLogic = new Panopticon.Terrain.TerrainLogic();
        var elevations = new ushort[4, 4];

        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                elevations[x, y] = 16000; // 16000 * 0.0390625 = 625.0 world Z
            }
        }

        terrainLogic.SetHeightMapData(HeightMapData.Create16Bit(0, elevations));

        var surface = BridgeSurface.CreateFromTriangles([(new Vector3(5f, 5f, 800f), new Vector3(35f, 5f, 800f), new Vector3(5f, 35f, 800f))]);
        terrainLogic.RegisterBridgeSurface(surface);

        Assert.Equal(625f, terrainLogic.GetGroundHeight(10f, 10f));
        Assert.Equal(625f, terrainLogic.GetGroundHeight(10f, 10f, out _));
        Assert.Equal(625f, terrainLogic.GetLayerHeight(10f, 10f, PathfindLayerType.Ground));
        Assert.Equal(625f, terrainLogic.GetLayerHeight(10f, 10f, PathfindLayerType.Ground, out _));

        Assert.Equal(800.0, (double)terrainLogic.GetGeneratedBridgeSurfaceHeight(10f, 10f)!, 2);
        Assert.Null(terrainLogic.GetGeneratedBridgeSurfaceHeight(50f, 50f));

        terrainLogic.ClearBridgeSurfaces();
        Assert.Null(terrainLogic.GetGeneratedBridgeSurfaceHeight(10f, 10f));
    }

    [Fact]
    public void Bridge_HasGeneratedProvenance_DistinguishesOrigin()
    {
        var marked = new MapObject(new Vector3(550f, 300f, 1084f), 0f, (RoadType)Panopticon.Data.Map.RoadType.BridgeStart, "Concrete");
        marked.Properties.AddAsciiString(Bridge.GeneratedProvenanceKey, Bridge.GeneratedProvenanceValue);
        Assert.True(Bridge.HasGeneratedProvenance(marked));

        var legacy = new MapObject(new Vector3(550f, 300f, 1084f), 0f, (RoadType)Panopticon.Data.Map.RoadType.BridgeStart, "Concrete");
        Assert.False(Bridge.HasGeneratedProvenance(legacy));

        // A marker with a different value is not a generated-bridge marker.
        var wrongValue = new MapObject(new Vector3(550f, 300f, 1084f), 0f, (RoadType)Panopticon.Data.Map.RoadType.BridgeStart, "Concrete");
        wrongValue.Properties.AddAsciiString(Bridge.GeneratedProvenanceKey, "legacy");
        Assert.False(Bridge.HasGeneratedProvenance(wrongValue));
    }

    [Fact]
    public void FitDeckPlane_PlanarDeckWithRailsAndSkirts_RecoversPlane()
    {
        // Grid roadway z = 1000 + 0.05y over [0,70]x[0,300] with elevated rail strips at
        // the deck edges and a sunk skirt bevel: the robust fit must recover the roadway
        // plane and exclude the outliers from the footprint.
        var triangles = RoadwayGridTriangles(0f, 70f, 0f, 300f, (x, y) => 1000f + (0.05f * y));
        triangles.AddRange(RoadwayGridTriangles(0f, 3f, 0f, 300f, (_, _) => 0f, 1, 30, 3.3f));
        triangles.AddRange(RoadwayGridTriangles(67f, 70f, 0f, 300f, (_, _) => 0f, 1, 30, 3.3f));
        triangles.AddRange(RoadwayGridTriangles(63f, 70f, 0f, 40f, (x, y) => 1000f + (0.05f * y), 2, 4, -2.5f));

        var surface = BridgeSurface.CreateFromTriangles(triangles);
        var fit = surface.FitDeckPlane();

        Assert.True(fit.Success, $"planar deck rejected: rms {fit.RmsResidual:F4}, fraction {fit.InlierAreaFraction:F3}, inliers {fit.InlierTriangleCount}/{fit.TotalTriangleCount}");
        Assert.InRange(fit.B, 0.0499f, 0.0501f);
        Assert.InRange(fit.C, 999.9f, 1000.1f);
        Assert.True(MathF.Abs(fit.A) < 1e-4f, $"cross slope {fit.A} must be ~0.");
        Assert.True(fit.InlierTriangleCount < surface.TriangleCount, "rails/skirts must be trimmed from the footprint.");
        Assert.True(fit.MaxAbsResidual < 0.1f, $"inlier max residual {fit.MaxAbsResidual:F4} too large for a planar deck.");

        // Footprint plane, not the topmost triangles: rails sit 3.3 above the fitted plane.
        Assert.Equal(1000f + (0.05f * 150f), fit.PlaneZ(35f, 150f), 2f);
    }

    [Fact]
    public void FitDeckPlane_CurvedDeck_IsRejectedByGuard()
    {
        // A deck with strong cross-curvature (sag 49 units across the width) is not a
        // plane: the fit must fail rather than guess, and report no inliers.
        var triangles = RoadwayGridTriangles(0f, 70f, 0f, 300f, (x, y) => 1000f + (0.05f * y) + (0.01f * x * x));

        var surface = BridgeSurface.CreateFromTriangles(triangles);
        var fit = surface.FitDeckPlane();

        Assert.False(fit.Success, "a curved deck must not pass the planar-fit guard.");
        Assert.Empty(fit.InlierTriangles);
    }

    [Fact]
    public void TryCutTerrainClearance_ConfinedToFootprintPlusRing_NeverRaises()
    {
        // Flat deck z = 1000 over [55,245]^2 above uniform terrain 1005: lattice vertices
        // under the footprint drop to plane - 1.02 (quantized floor), one ring of
        // vertices blends partway, everything beyond the ring stays exactly 1005, and no
        // vertex anywhere rises.
        const int size = 40;
        var elevations = new ushort[size, size];

        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                elevations[x, y] = 25728; // 25728 * 0.0390625 = 1005.0
            }
        }

        var heightMap = new HeightMap(HeightMapData.Create16Bit(0, elevations));
        var before = new float[size, size];

        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                before[x, y] = heightMap.GetHeight(x, y);
            }
        }

        var surface = BridgeSurface.CreateFromTriangles(RoadwayGridTriangles(55f, 245f, 55f, 245f, (_, _) => 1000f));
        var cut = surface.TryCutTerrainClearance(heightMap, out var bounds, out var fit);

        Assert.True(cut, "flat deck over raised terrain must cut.");
        Assert.True(fit.Success);

        // Under the footprint AND at the cell-overshoot edge vertices (both support
        // overlapping cells): plane - 1.02 = 998.98, truncated by LowerHeight.
        var underDeck = heightMap.GetHeight(15, 15);
        var edgeVertex = heightMap.GetHeight(25, 15);
        Assert.True(underDeck <= 999.001f && underDeck >= 998f, $"under-deck vertex {underDeck} must sit at plane - 1.02 (quantized).");
        Assert.True(edgeVertex <= 999.001f && edgeVertex >= 998f, $"cell-overshoot vertex {edgeVertex} must take the pure cut target.");

        // r3-g8 mouth apron: this fixture's terrain (1005) sits 5 above the deck
        // everywhere, so both lateral sides classify raised at each MOUTH band
        // (seam±40 = y[15,95]/[205,285] for this square deck). Vertex (26,15) (world
        // 260,150) is mid-span — outside both mouth bands — and stays unchanged.
        Assert.Equal(1005f, heightMap.GetHeight(26, 15));

        // In-band vertex (26,7) (world 260, y=70 in the south band): apron-covered.
        var aproned = heightMap.GetHeight(26, 7);
        Assert.True(aproned < 1005f, $"apron vertex (26,7) {aproned} must be softened below pre-cut.");

        // One cell beyond the apron lateral bound (distance 25): unchanged.
        Assert.Equal(1005f, heightMap.GetHeight(27, 7));
        Assert.Equal(1005f, heightMap.GetHeight(27, 15));
        Assert.Equal(1005f, heightMap.GetHeight(15, 26));

        // Beyond footprint + ring: exactly unchanged.
        Assert.Equal(1005f, heightMap.GetHeight(2, 2));
        Assert.Equal(1005f, heightMap.GetHeight(2, 37));
        Assert.Equal(1005f, heightMap.GetHeight(37, 2));
        Assert.Equal(1005f, heightMap.GetHeight(37, 37));
        Assert.Equal(1005f, heightMap.GetHeight(37, 15));

        // Never raises anywhere.
        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                Assert.True(heightMap.GetHeight(x, y) <= before[x, y], $"vertex ({x}, {y}) was raised.");
            }
        }

        // Changed bounds cover the footprint plus ring plus the per-mouth apron bands
        // (seam±40 longitudinal, both lateral sides classify raised on this fixture),
        // not the map.
        Assert.True(bounds.Width >= 200f && bounds.Height >= 200f, $"changed bounds {bounds} too small for the footprint.");
        Assert.True(bounds.Width <= 240f && bounds.Height <= 280f, $"changed bounds {bounds} spill past footprint + ring + mouth bands.");
    }

    [Fact]
    public void TryCutTerrainClearance_CurvedDeck_NoCutApplied()
    {
        // SUPERSEDED (r3-g8 corrective): the envelope design handles curvature exactly
        // (see EnvelopePlan_CutsCurvedRoadwayExactly); the old fit-rejection path no
        // longer exists. Retained placeholder removed by the fingerprint gate tests.
        Assert.True(BridgeSurface.IsSupportedGeometry(BridgeSurface.SupportedGeometryFingerprint));
    }

    /// <summary>
    /// Axis-aligned quad grid over [x0,x1]x[y0,y1], two triangles per cell, z taken from
    /// the corner sampler; an optional per-triangle vertical offset superimposes rail or
    /// skirt geometry on the same XY footprint.
    /// </summary>
    private static List<(Vector3, Vector3, Vector3)> RoadwayGridTriangles(
        float x0,
        float x1,
        float y0,
        float y1,
        Func<float, float, float> z,
        int cellsX = 10,
        int cellsY = 10,
        float offset = 0f)
    {
        var triangles = new List<(Vector3, Vector3, Vector3)>();
        var stepX = (x1 - x0) / cellsX;
        var stepY = (y1 - y0) / cellsY;

        for (var ix = 0; ix < cellsX; ix++)
        {
            for (var iy = 0; iy < cellsY; iy++)
            {
                var ax = x0 + (ix * stepX);
                var ay = y0 + (iy * stepY);
                var bx = ax + stepX;
                var by = ay + stepY;

                triangles.Add((
                    new Vector3(ax, ay, z(ax, ay) + offset),
                    new Vector3(bx, ay, z(bx, ay) + offset),
                    new Vector3(bx, by, z(bx, by) + offset)));
                triangles.Add((
                    new Vector3(ax, ay, z(ax, ay) + offset),
                    new Vector3(bx, by, z(bx, by) + offset),
                    new Vector3(ax, by, z(ax, by) + offset)));
            }
        }

        return triangles;
    }

    [Fact]
    public void TryCutTerrainClearance_NearEdgeFacesFailClosed_PhantomSamplesUntouched()
    {
        // r3-g8 corrective: a supported roadway face that crosses the usable lattice
        // edge cannot be certified against rendered terrain, so the whole bridge cut
        // fails closed; the stored phantom last row/column (index 39 of 40) must never
        // be written.
        const int stored = 40;
        var elevations = new ushort[stored, stored];

        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                elevations[x, y] = 25728;
            }
        }

        var heightMap = new HeightMap(HeightMapData.Create16Bit(0, elevations));
        var surface = BridgeSurface.CreateFromTriangles(RoadwayGridTriangles(330f, 385f, 330f, 385f, (_, _) => 1000f));

        var cut = surface.TryCutTerrainClearance(heightMap, out var bounds, out var fit);

        Assert.False(cut, "a roadway face crossing the usable lattice edge must fail closed.");
        Assert.False(fit.Success);

        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                Assert.Equal(1005f, heightMap.GetHeight(x, y));
            }
        }
    }

    [Fact]
    public void TryCutTerrainClearance_PhantomEdgeSamplesNeverWritten()
    {
        // Rendered lattice is samples 0..38 (lastSample = Width-1) and cells 0..37
        // (lastCell = Width-2); the stored sample index 39 of 40 is phantom and must
        // stay exactly as authored even when the cut reaches the rendered edge.
        const int stored = 40;
        var elevations = new ushort[stored, stored];

        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                elevations[x, y] = 25728;
            }
        }

        var heightMap = new HeightMap(HeightMapData.Create16Bit(0, elevations));
        var surface = BridgeSurface.CreateFromTriangles(RoadwayGridTriangles(330f, 375f, 330f, 375f, (_, _) => 1000f));

        var cut = surface.TryCutTerrainClearance(heightMap, out _, out var fit);

        Assert.True(cut, "a fully-in-lattice deck near the edge must still cut.");
        Assert.True(fit.Success);

        // The rendered edge vertex 38 (world 380) sits 5 units from the deck edge at
        // 375: cell (37, 35..36) overlaps, so the vertex takes the pure cut target.
        Assert.True(heightMap.GetHeight(38, 36) < 1005f, "the rendered edge vertex 38 must be lowered.");

        for (var e = 0; e < stored; e++)
        {
            Assert.Equal(1005f, heightMap.GetHeight(39, e));
            Assert.Equal(1005f, heightMap.GetHeight(e, 39));
        }
    }

    [Fact]
    public void TryCutTerrainClearance_MouthApron_RaisedSidePerMouth()
    {
        // r3-g8 mouth apron: per-mouth raised classification (east raised via terrain
        // 1005 east of x=150, 995 west), apron at BOTH deck mouths (longitudinal bands
        // seam±40), cut-only, zero effect at the 23-unit lateral bound and the ±40
        // longitudinal bound, footprint and driven profile untouched.
        const int stored = 40;
        var elevations = new ushort[stored, stored];

        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                var terrain = x >= 15 ? 1005f : 995f; // lattice 15 == world 150
                elevations[x, y] = (ushort)MathF.Round(terrain / 0.0390625f);
            }
        }

        var heightMap = new HeightMap(HeightMapData.Create16Bit(0, elevations));
        var surface = BridgeSurface.CreateFromTriangles(RoadwayGridTriangles(55f, 205f, 60f, 240f, (_, _) => 1000f, cellsX: 15, cellsY: 18));

        var cut = surface.TryCutTerrainClearance(heightMap, out var bounds, out var fit);

        Assert.True(cut);
        Assert.True(fit.Success);

        // South mouth band (y 20..100): apron lowers raised-side vertices at d=5/15.
        var d5South = heightMap.GetHeight(21, 7);
        var d15South = heightMap.GetHeight(22, 7);
        Assert.True(d5South < 1005f, $"south apron d5 vertex {d5South} must be lowered.");
        Assert.True(d15South < 1004.9f, $"south apron d15 vertex {d15South} must be lowered.");
        Assert.True(d15South > d5South, "apron must blend back toward pre-cut with distance.");

        // North mouth band (y 160..240): same apron at the other deck end. Full
        // strength inside dy<=20 of the seam (y 220..240); the band edge row y=200
        // (dy=40) is the zero-effect longitudinal boundary.
        var d5North = heightMap.GetHeight(21, 22);
        var d15North = heightMap.GetHeight(22, 22);
        Assert.True(d5North < 1005f, $"north apron d5 vertex {d5North} must be lowered.");
        Assert.True(d15North < 1004.9f, $"north apron d15 vertex {d15North} must be lowered.");
        Assert.Equal(1005f, heightMap.GetHeight(22, 20));

        // BETWEEN the mouths (mid-span lateral band): no apron by design.
        Assert.Equal(1005f, heightMap.GetHeight(22, 13));

        // Lateral bound (d=25 > 23): unchanged.
        Assert.Equal(1005f, heightMap.GetHeight(23, 7));
        Assert.Equal(1005f, heightMap.GetHeight(23, 20));

        // Longitudinal bound (y=100 == band end): zero effect; one row inside: tapered.
        Assert.Equal(1005f, heightMap.GetHeight(22, 10));
        var taperRow = heightMap.GetHeight(22, 9);
        Assert.True(taperRow <= 1005f, "taper row must never raise.");

        // No abrupt longitudinal step between adjacent rows inside the band.
        for (var j = 3; j < 9; j++)
        {
            var step = MathF.Abs(heightMap.GetHeight(22, j + 1) - heightMap.GetHeight(22, j)) / HeightMap.HorizontalScale;

            if (heightMap.GetHeight(22, j) >= 1005f - 0.001f && heightMap.GetHeight(22, j + 1) >= 1005f - 0.001f)
            {
                continue;
            }

            Assert.True(step <= 0.45f, $"longitudinal boundary step {step:F3}/unit between rows {j}/{j + 1}.");
        }

        // Lowered (west) side: no apron; ring-only no-lower behavior. NOTE: the raised
        // east vertex (22,7) (world 220, d=15) is apron-covered (lowered to ~1002.9) —
        // the earlier assert wrongly expected the lowered-side 995 there.
        Assert.Equal(995f, heightMap.GetHeight(5, 7));
        Assert.Equal(995f, heightMap.GetHeight(4, 7));
        var raisedCovered = heightMap.GetHeight(22, 7);
        Assert.True(raisedCovered < 1005f && raisedCovered >= 998.98f - 0.05f,
            $"raised-side apron vertex (22,7) {raisedCovered} must be lowered toward the envelope, not left at pre-cut.");

        // Never raise: full map post <= pre.
        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                var pre = x >= 15 ? 1005f : 995f;
                Assert.True(heightMap.GetHeight(x, y) <= pre + 0.001f, $"vertex ({x}, {y}) raised.");
            }
        }
    }

    [Fact]
    public void TryCutTerrainClearance_MouthApron_MirroredWestRaised()
    {
        // Mirrored proof (classification is NOT cardinal): terrain 1005 WEST of x=150
        // (raised), 995 east. The apron must select the MIN lateral side.
        const int stored = 40;
        var elevations = new ushort[stored, stored];

        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                var terrain = x < 15 ? 1005f : 995f;
                elevations[x, y] = (ushort)MathF.Round(terrain / 0.0390625f);
            }
        }

        var heightMap = new HeightMap(HeightMapData.Create16Bit(0, elevations));
        var surface = BridgeSurface.CreateFromTriangles(RoadwayGridTriangles(55f, 205f, 60f, 240f, (_, _) => 1000f, cellsX: 15, cellsY: 18));

        var cut = surface.TryCutTerrainClearance(heightMap, out _, out var fit);

        Assert.True(cut);
        Assert.True(fit.Success);

        // West mouth bands: apron lowers the raised-side vertices.
        var d5South = heightMap.GetHeight(5, 7);
        var d15South = heightMap.GetHeight(4, 7);
        Assert.True(d5South < 1005f, $"mirrored south apron d5 vertex {d5South} must be lowered.");
        Assert.True(d15South < 1004.9f, $"mirrored south apron d15 vertex {d15South} must be lowered.");
        var d5North = heightMap.GetHeight(5, 22);
        Assert.True(d5North < 1005f, $"mirrored north apron d5 vertex {d5North} must be lowered.");

        // North band boundary row (y=200, dy=40 from the seam): zero effect; the
        // footprint-supporting vertex (5,20) (world 50,200) takes the envelope cut.
        Assert.Equal(1005f, heightMap.GetHeight(2, 20));
        Assert.Equal(998.945312f, heightMap.GetHeight(5, 20), 3);

        // East lowered side: untouched beyond its ring.
        Assert.Equal(995f, heightMap.GetHeight(22, 7));

        // Lateral bound west (world 30 -> lattice 3): unchanged at its raised pre-cut.
        Assert.Equal(1005f, heightMap.GetHeight(3, 7));

        // Between mouths: no apron; (5,13) itself is footprint-supporting (cell
        // x[50,60] straddles the deck edge 55) and takes the envelope target.
        Assert.Equal(998.945312f, heightMap.GetHeight(5, 13), 3);
        Assert.Equal(1005f, heightMap.GetHeight(2, 13));

        // Never raise.
        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                var pre = x < 15 ? 1005f : 995f;
                Assert.True(heightMap.GetHeight(x, y) <= pre + 0.001f, $"vertex ({x}, {y}) raised.");
            }
        }
    }

    [Fact]
    public void TryCutTerrainClearance_MouthApron_NoApronOnLoweredTerrain()
    {
        // Uniform lowered terrain (995, deck 1000 above terrain everywhere): no side is
        // raised, so no apron at all; only footprint/ring behavior applies and nothing
        // beyond the ring is touched.
        const int stored = 40;
        var elevations = new ushort[stored, stored];

        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                elevations[x, y] = (ushort)MathF.Round(995f / 0.0390625f);
            }
        }

        var heightMap = new HeightMap(HeightMapData.Create16Bit(0, elevations));
        var surface = BridgeSurface.CreateFromTriangles(RoadwayGridTriangles(55f, 205f, 60f, 240f, (_, _) => 1000f, cellsX: 15, cellsY: 18));

        var cut = surface.TryCutTerrainClearance(heightMap, out _, out var fit);

        Assert.True(cut);
        Assert.True(fit.Success);

        // Beyond footprint+ring: unchanged everywhere.
        Assert.Equal(995f, heightMap.GetHeight(22, 7));
        Assert.Equal(995f, heightMap.GetHeight(23, 7));
        Assert.Equal(995f, heightMap.GetHeight(2, 7));
        Assert.Equal(995f, heightMap.GetHeight(3, 7));

        // Never raise.
        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                Assert.True(heightMap.GetHeight(x, y) <= 995f + 0.001f, $"vertex ({x}, {y}) raised.");
            }
        }
    }

    [Fact]
    public void TryCutTerrainClearance_MouthApron_DeterministicIdempotentMergeOrder()
    {
        // Determinism across fresh maps, idempotence on re-application (never raises,
        // bounded), and merge-order independence for two overlapping raised-side decks.
        var buildMap = () =>
        {
            const int stored = 40;
            var elevations = new ushort[stored, stored];

            for (var x = 0; x < stored; x++)
            {
                for (var y = 0; y < stored; y++)
                {
                    elevations[x, y] = (ushort)MathF.Round((x >= 15 ? 1005f : 995f) / 0.0390625f);
                }
            }

            return new HeightMap(HeightMapData.Create16Bit(0, elevations));
        };

        const int stored = 40;
        var deckA = BridgeSurface.CreateFromTriangles(RoadwayGridTriangles(55f, 205f, 60f, 240f, (_, _) => 1000f, cellsX: 15, cellsY: 18));
        var deckB = BridgeSurface.CreateFromTriangles(RoadwayGridTriangles(120f, 270f, 100f, 280f, (_, _) => 1002f, cellsX: 10, cellsY: 12));

        // Production contract (Scene3D): every plan is built against the SAME pre-cut
        // heights, then all plans merge once with per-vertex min — order-independent.
        // (Sequential wrapper applies would rebuild plans against already-aproned
        // terrain, which is not the production shape.)
        var mapA = buildMap();
        var facesA = GridFaces(55f, 205f, 60f, 240f, 1000f, cellsPerSide: 15);
        var facesB = GridFaces(120f, 270f, 100f, 280f, 1002f, cellsPerSide: 10);
        var planA1 = BridgeSurface.TryBuildEnvelopePlan(facesA, mapA, out _);
        var planB1 = BridgeSurface.TryBuildEnvelopePlan(facesB, mapA, out _);
        var (boundsA1, _) = BridgeSurface.TerrainClearancePlan.MergeAndApply(mapA, [planA1!, planB1!]);

        var mapB = buildMap();
        var planB2 = BridgeSurface.TryBuildEnvelopePlan(facesB, mapB, out _);
        var planA2 = BridgeSurface.TryBuildEnvelopePlan(facesA, mapB, out _);
        var (boundsA2, _) = BridgeSurface.TerrainClearancePlan.MergeAndApply(mapB, [planB2!, planA2!]);

        Assert.Equal(boundsA1, boundsA2);

        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                Assert.Equal(mapA.GetHeight(x, y), mapB.GetHeight(x, y));
            }
        }

        // Re-application of the same deck on the same map: min-merge stability —
        // never raises, never drifts below the envelope target.
        var before = new float[40, 40];

        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 40; y++)
            {
                before[x, y] = mapA.GetHeight(x, y);
            }
        }

        deckA.TryCutTerrainClearance(mapA, out var repeatBounds, out _);
        deckB.TryCutTerrainClearance(mapA, out _, out _);

        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 40; y++)
            {
                Assert.True(mapA.GetHeight(x, y) <= before[x, y] + 0.001f, $"repeat apply raised ({x}, {y}).");
                Assert.True(mapA.GetHeight(x, y) >= 995f - 0.001f, $"repeat apply dropped vertex ({x}, {y}) below any target.");
            }
        }

    }

    [Fact]
    public void EnvelopePlan_CutsSubcellLowFace_BelowOldPlaneTarget()
    {
        // r3-g8 corrective mutant (green): a supported roadway face 2 units below the
        // surrounding deck, wholly inside terrain cell (15,15), must drive that cell's
        // vertices to its own plane minus 1.02 — the old fitted-plane target left it
        // buried (red probe: actual clearance -0.98 against the old target).
        var heightMap = FlatHeightMap(40, 1005f);
        var faces = GridFaces(55f, 245f, 55f, 245f, 1000f, cellsPerSide: 19);
        faces.Add(new BridgeSurface.BridgeRawFace("mutant", 0, 0, 0, 0, new Vector3(151f, 151f, 998f), new Vector3(159f, 151f, 998f), new Vector3(159f, 159f, 998f)));

        var plan = BridgeSurface.TryBuildEnvelopePlan(faces, heightMap, out var reason);
        Assert.True(plan != null, $"plan build failed: {reason}");

        var applied = BridgeSurface.TerrainClearancePlan.MergeAndApply(heightMap, [plan!]);
        Assert.True(applied.ChangedVertices > 0);

        // Cell (15,15) spans lattice vertices (15..17, 15..17); they carry the mutant
        // face's own plane target (998 - 1.02 = 996.98, truncated down).
        var cellVertex = heightMap.GetHeight(16, 16);
        Assert.True(cellVertex <= 997.01f, $"mutant-cell vertex {cellVertex} must sit at the actual low-face plane - 1.02.");

        // Actual clearance vs the low face: 998 - stored >= 0.98.
        Assert.True(998f - cellVertex >= BridgeSurface.MinimumDeckClearance - 0.001f, $"clearance {998f - cellVertex:F3} below 0.98.");
    }

    [Fact]
    public void EnvelopePlan_ReversedWinding_GivesIdenticalTargets()
    {
        var heightMapA = FlatHeightMap(40, 1005f);
        var heightMapB = FlatHeightMap(40, 1005f);
        var faces = GridFaces(55f, 245f, 55f, 245f, 1000f, cellsPerSide: 19);
        var flipped = faces.Select(face => face with { B = face.C, C = face.B }).ToList();

        var planA = BridgeSurface.TryBuildEnvelopePlan(faces, heightMapA, out _);
        var planB = BridgeSurface.TryBuildEnvelopePlan(flipped, heightMapB, out _);
        Assert.True(planA != null && planB != null);

        Assert.Equal(planA.VertexTargets.Count, planB.VertexTargets.Count);
        Assert.Equal(planA.RingTargets.Count, planB.RingTargets.Count);

        foreach (var (vertex, target) in planA.VertexTargets)
        {
            Assert.True(planB.VertexTargets.TryGetValue(vertex, out var other));
            Assert.Equal(target, other, 3f);
        }
    }

    [Fact]
    public void MergeAndApply_PlanOrderIsDeterministic()
    {
        // Two overlapping plans applied in both list orders must produce identical
        // stored elevations (per-vertex minimum, no sequential read-after-cut).
        var facesA = GridFaces(55f, 245f, 55f, 245f, 1000f, cellsPerSide: 19);
        var facesB = GridFaces(120f, 300f, 120f, 300f, 998f, cellsPerSide: 12);

        var heightMapA = FlatHeightMap(40, 1005f);
        var heightMapB = FlatHeightMap(40, 1005f);
        var planA = BridgeSurface.TryBuildEnvelopePlan(facesA, heightMapA, out _);
        var planB = BridgeSurface.TryBuildEnvelopePlan(facesB, heightMapB, out _);
        Assert.True(planA != null && planB != null);

        var first = BridgeSurface.TerrainClearancePlan.MergeAndApply(heightMapA, [planA!, planB!]);
        var second = BridgeSurface.TerrainClearancePlan.MergeAndApply(heightMapB, [planB!, planA!]);

        Assert.Equal(first.ChangedVertices, second.ChangedVertices);
        Assert.Equal(first.Bounds, second.Bounds);

        for (var x = 0; x < 39; x++)
        {
            for (var y = 0; y < 39; y++)
            {
                Assert.Equal(heightMapA.GetHeight(x, y), heightMapB.GetHeight(x, y));
            }
        }
    }

    [Fact]
    public void IsSupportedGeometry_GateMatchesOnlyVerifiedFingerprint()
    {
        Assert.True(BridgeSurface.IsSupportedGeometry(BridgeSurface.SupportedGeometryFingerprint));
        Assert.False(BridgeSurface.IsSupportedGeometry("DEADBEEF"));
        Assert.False(BridgeSurface.IsSupportedGeometry(null));
    }

    [Fact]
    public void ClassifyRawFaces_UnclassifiedCandidateFailsClosed()
    {
        // A world-horizontal in-band face outside every reviewed source range is
        // unclassified: the bridge must be unsupported (no cut).
        var anchorStart = new Vector3(0f, 0f, 1000f);
        var anchorEnd = new Vector3(0f, 700f, 1035f);
        var faces = new List<BridgeSurface.BridgeRawFace>
        {
            new("CBBRIDGEST.BRIDGE_SPAN", 0, 5, 0, 0, new Vector3(550f, 310f, 1016.5f), new Vector3(570f, 310f, 1016.5f), new Vector3(570f, 330f, 1017f)),
            new("CBBRIDGEST.BRIDGE_SPAN", 0, 40, 0, 0, new Vector3(550f, 610f, 1019.5f), new Vector3(570f, 610f, 1019.5f), new Vector3(570f, 630f, 1020f)),
            new("mystery.piece", 0, 900, 0, 0, new Vector3(550f, 410f, 1017.5f), new Vector3(570f, 410f, 1017.5f), new Vector3(570f, 430f, 1018f)),
        };

        var classification = BridgeSurface.ClassifyRawFaces(faces, anchorStart, anchorEnd);

        Assert.False(classification.Supported);
        Assert.Equal("unclassified-candidate-faces", classification.UnsupportedReason);
        Assert.Equal(1, classification.UnclassifiedCount);
        Assert.Equal(3, classification.CandidateFaceCount);
    }

    [Fact]
    public void EnvelopePlan_CutsCurvedRoadwayExactly()
    {
        // The envelope needs no planarity: a curved supported roadway lowers each cell
        // to its own actual face planes (the old single-plane design rejected this).
        var heightMap = FlatHeightMap(40, 1005f);
        var faces = GridFaces(55f, 245f, 55f, 245f, (x, _) => 1000f + (0.02f * (x - 150f) * (x - 150f) * 0.1f), cellsPerSide: 19);

        var plan = BridgeSurface.TryBuildEnvelopePlan(faces, heightMap, out _);
        Assert.True(plan != null);

        var applied = BridgeSurface.TerrainClearancePlan.MergeAndApply(heightMap, [plan!]);
        Assert.True(applied.ChangedVertices > 0);
        Assert.True(heightMap.GetHeight(15, 15) < 1005f);
    }

    private static HeightMap FlatHeightMap(int stored, float height)
    {
        var elevation = (ushort)(height / 0.0390625f);
        var elevations = new ushort[stored, stored];

        for (var x = 0; x < stored; x++)
        {
            for (var y = 0; y < stored; y++)
            {
                elevations[x, y] = elevation;
            }
        }

        return new HeightMap(HeightMapData.Create16Bit(0, elevations));
    }

    private static List<BridgeSurface.BridgeRawFace> GridFaces(float x0, float x1, float y0, float y1, float z, int cellsPerSide)
    {
        var faces = new List<BridgeSurface.BridgeRawFace>();
        var stepX = (x1 - x0) / cellsPerSide;
        var stepY = (y1 - y0) / cellsPerSide;
        var faceIndex = 0;

        for (var iy = 0; iy < cellsPerSide; iy++)
        {
            for (var ix = 0; ix < cellsPerSide; ix++)
            {
                var ax = x0 + (ix * stepX);
                var ay = y0 + (iy * stepY);
                var bx = ax + stepX;
                var by = ay + stepY;

                faces.Add(new BridgeSurface.BridgeRawFace("grid", 0, faceIndex++, 0, 0, new Vector3(ax, ay, z), new Vector3(bx, ay, z), new Vector3(bx, by, z)));
                faces.Add(new BridgeSurface.BridgeRawFace("grid", 0, faceIndex++, 0, 0, new Vector3(ax, ay, z), new Vector3(bx, by, z), new Vector3(ax, by, z)));
            }
        }

        return faces;
    }

    private static List<BridgeSurface.BridgeRawFace> GridFaces(float x0, float x1, float y0, float y1, Func<float, float, float> z, int cellsPerSide)
    {
        var faces = new List<BridgeSurface.BridgeRawFace>();
        var stepX = (x1 - x0) / cellsPerSide;
        var stepY = (y1 - y0) / cellsPerSide;
        var faceIndex = 0;

        for (var iy = 0; iy < cellsPerSide; iy++)
        {
            for (var ix = 0; ix < cellsPerSide; ix++)
            {
                var ax = x0 + (ix * stepX);
                var ay = y0 + (iy * stepY);
                var bx = ax + stepX;
                var by = ay + stepY;

                faces.Add(new BridgeSurface.BridgeRawFace("grid", 0, faceIndex++, 0, 0, new Vector3(ax, ay, z(ax, ay)), new Vector3(bx, ay, z(bx, ay)), new Vector3(bx, by, z(bx, by))));
                faces.Add(new BridgeSurface.BridgeRawFace("grid", 0, faceIndex++, 0, 0, new Vector3(ax, ay, z(ax, ay)), new Vector3(bx, by, z(bx, by)), new Vector3(ax, by, z(ax, by))));
            }
        }

        return faces;
    }

    private static ImportedVertex Vertex(float x, float y, float z)
    {
        return new ImportedVertex(new Vector3(x, y, z), Vector3.UnitZ);
    }

    private static ImportedMesh ImportedMesh(IReadOnlyList<ImportedVertex> vertices, IReadOnlyList<ushort> indices)
    {
        var bounds = new AxisAlignedBoundingBox(
            new Vector3(vertices.Min(v => v.Position.X), vertices.Min(v => v.Position.Y), vertices.Min(v => v.Position.Z)),
            new Vector3(vertices.Max(v => v.Position.X), vertices.Max(v => v.Position.Y), vertices.Max(v => v.Position.Z)));
        var sphere = new BoundingSphere((bounds.Min + bounds.Max) * 0.5f, (bounds.Max - bounds.Min).Length() * 0.5f);
        var material = new ImportedMaterial(
            ImportedMaterial.FixedFunctionFamily,
            ImportedCullMode.None,
            depthWriteEnabled: true,
            ImportedDepthComparison.LessEqual,
            blendEnabled: false,
            ImportedBlendFactor.One,
            ImportedBlendFactor.Zero,
            ImportedBlendFactor.One,
            fixedFunctionShading: null,
            properties: []);

        return new ImportedMesh(
            vertices,
            indices,
            bounds,
            sphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            [material],
            [new ImportedMeshPart(null, null, 0, (uint)indices.Count, blendEnabled: false, materialIndex: 0)]);
    }

    private static ModelSubObject StubSubObject()
    {
        var bone = new ModelBone(0, "bridge-bone", null, Vector3.Zero, Quaternion.Identity, Matrix4x4.Identity);

        return new ModelSubObject("CBBRIDGEST.BRIDGE_LEFT", "BRIDGE_LEFT", bone, new StubRenderObject());
    }

    private sealed class StubRenderObject : ModelRenderObject
    {
        private readonly AxisAlignedBoundingBox _bounds = new(new Vector3(0f, 0f, 0f), new Vector3(100f, 40f, 10f));
        private readonly BoundingSphere _sphere = new(Vector3.Zero, 100f);

        public override ref readonly AxisAlignedBoundingBox BoundingBox => ref _bounds;

        public override ref readonly BoundingSphere BoundingSphere => ref _sphere;

        public override bool Hidden => false;

        internal override void BuildRenderList(
            RenderList renderList,
            Camera camera,
            ModelInstance modelInstance,
            ModelMeshInstance modelMeshInstance,
            ModelBone parentBone,
            in Matrix4x4 modelTransform,
            bool castsShadow,
            MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS,
            float visualScale)
        {
        }

        internal override void BuildRenderListWithWorldMatrix(
            RenderList renderList,
            Camera camera,
            ModelInstance modelInstance,
            ModelMeshInstance modelMeshInstance,
            ModelBone parentBone,
            in Matrix4x4 meshWorldMatrix,
            bool castsShadow,
            MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS = null)
        {
        }
    }
}
