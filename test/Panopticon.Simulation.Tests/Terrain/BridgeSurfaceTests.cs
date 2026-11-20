#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
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
    public void Create_NoRoadwayTriangles_ReturnsNull()
    {
        var importedMesh = ImportedMesh(
            vertices:
            [
                Vertex(0f, 0f, AnchorZ - 40f),
                Vertex(20f, 0f, AnchorZ - 40f),
                Vertex(20f, 10f, AnchorZ - 40f),
            ],
            // Wound so the face normal points down: horizontal but below the band floor.
            indices: [0, 2, 1]);

        var surface = BridgeSurface.Create(
            [Tuple.Create(StubSubObject(), Matrix4x4.Identity)],
            _ => importedMesh,
            new Vector3(0f, 0f, AnchorZ),
            new Vector3(200f, 0f, AnchorZ));

        Assert.Null(surface);
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
        private readonly AxisAlignedBoundingBox _bounds = new(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
        private readonly BoundingSphere _sphere = new(Vector3.Zero, 1f);

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
