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
        System.IO.File.AppendAllText("/tmp/opencode/gd5.txt", "pitched=" + measured.Value.ToString("F5") + "\n");
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
        System.IO.File.AppendAllText("/tmp/opencode/gd5.txt", "pitched=" + measured.Value.ToString("F5") + "\n");
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
        System.IO.File.AppendAllText("/tmp/opencode/gd5.txt", "flat=" + measured.Value.ToString("F5") + "\n");
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
