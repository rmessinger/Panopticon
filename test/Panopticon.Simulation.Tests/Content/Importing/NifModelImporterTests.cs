using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.IO;
using Panopticon.Tests.Content;
using Panopticon.Tests.Data.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;

namespace Panopticon.Tests.Content.Importing;

public sealed class NifModelImporterTests
{
    private static ImportedModel? Import(
        byte[] fileBytes,
        string name = "test_model",
        bool omitFile = false)
    {
        var importer = new NifModelImporter();
        var fileSystem = new MemoryFileSystem();
        if (!omitFile)
        {
            fileSystem.AddFile($"cache/nifcache/{name}.nifcache", fileBytes);
        }

        return importer.ImportModel(name, new AssetImportContext(fileSystem, language: null));
    }

    private static byte[] StaticModelBytes(
        (float X, float Y, float Z)? shapeTranslation = null,
        float shapeScale = 1.0f,
        int[]? shapeProperties = null)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var materialType = builder.AddBlockType("NiMaterialProperty");
        var textureType = builder.AddBlockType("NiTexturingProperty");
        var sourceTextureType = builder.AddBlockType("NiSourceTexture");
        var zBufferType = builder.AddBlockType("NiZBufferProperty");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [4]));
        builder.AddBlock(zBufferType, NifTestProperties.ZBuffer(flags: 3, function: 3));
        builder.AddBlock(materialType, NifTestProperties.Material(
            diffuse: (0.4f, 0.5f, 0.6f), specular: (0.25f, 0.25f, 0.25f)));
        builder.AddBlock(textureType, NifTestProperties.Texturing(
            baseSlot: (6, 3, 2, 0, 0, -100)));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(
            name: "hull",
            data: 5,
            translation: shapeTranslation,
            scale: shapeScale,
            properties: shapeProperties ?? [2, 3, 1]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)],
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddBlock(sourceTextureType, NifTestProperties.SourceTexture(fileName: "hull.tga"));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    [Fact]
    public void MissingEntryReturnsNull()
    {
        Assert.Null(Import([], omitFile: true));
    }

    [Fact]
    public void NullNameReturnsNull()
    {
        var importer = new NifModelImporter();
        var fileSystem = new MemoryFileSystem();
        Assert.Null(importer.ImportModel("", new AssetImportContext(fileSystem, null)));
    }

    [Fact]
    public void StaticModelProducesRootBoneAndNamedSubObject()
    {
        var model = Import(StaticModelBytes());

        Assert.NotNull(model);
        Assert.Equal("test_model", model.Name);
        Assert.Equal(ImportedSkeletonOrigin.Embedded, model.SkeletonOrigin);
        var bone = Assert.Single(model.Skeleton.Bones);
        Assert.Equal("root", bone.Name);
        Assert.Equal(-1, bone.ParentIndex);
        Assert.Equal(1f, bone.BindRotation.W);

        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal("hull", subObject.DisplayName);
        Assert.Equal(0, subObject.ParentBoneIndex);
    }

    [Fact]
    public void NodeHierarchyBecomesBonesAndRestPoseMatchesBakedWorld()
    {
        var model = Import(HierarchyModelBytes());

        Assert.Equal(2, model.Skeleton.Bones.Count);
        Assert.Equal("root", model.Skeleton.Bones[0].Name);
        Assert.Equal(-1, model.Skeleton.Bones[0].ParentIndex);
        Assert.Equal("child", model.Skeleton.Bones[1].Name);
        Assert.Equal(0, model.Skeleton.Bones[1].ParentIndex);

        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal(1, subObject.ParentBoneIndex);

        // Composing the conjugated bone locals must equal the conjugated
        // source world chain (C4a math), the deep-dive rest-pose parity.
        var file = FileFormats.Nif.NifFile.Read(new MemoryStream(HierarchyModelBytes()));
        var rootLocal = NifTransformConverter.CreateLocalMatrix((FileFormats.Nif.NifAvObject)file.Blocks[0]);
        var childLocal = NifTransformConverter.CreateLocalMatrix((FileFormats.Nif.NifAvObject)file.Blocks[1]);
        var expectedWorld = NifTransformConverter.ConjugateSourceToEngine(childLocal * rootLocal);
        var actualWorld = BoneLocal(model.Skeleton.Bones[1]) * BoneLocal(model.Skeleton.Bones[0]);
        AssertMatricesEqual(expectedWorld, actualWorld);

        // Baked bone-local vertices, transformed by the bone chain, land on
        // the C4a world bake: v * (L_child * L_root) * E.
        var mesh = (ImportedMesh)model.SubObjects[0].RenderObject;
        var bake = childLocal * rootLocal * SourceToEngineMatrix();
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            var expected = Vector3.Transform(SourceVertices[i], bake);
            var actual = Vector3.Transform(mesh.Vertices[i].Position, actualWorld);
            AssertEqual(expected, actual);
        }
    }

    [Fact]
    public void ScaledSubtreeBakesIntoNearestBoneAncestor()
    {
        var model = Import(ScaledSubtreeModelBytes());

        var bone = Assert.Single(model.Skeleton.Bones);
        Assert.Equal("root", bone.Name);
        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal(0, subObject.ParentBoneIndex);

        // The scaled child node (scale 2, translated (0, -1, 0)) bakes into
        // the root bone frame: v * [2I | (0,-1,0)] * E.
        var mesh = (ImportedMesh)subObject.RenderObject;
        const float f = 3.280839895f;
        // (1, 0, 0) -> (2, -1, 0) -> (1, 2, 0) * feet.
        AssertEqual(new Vector3(1 * f, 2 * f, 0), mesh.Vertices[1].Position);
    }

    [Fact]
    public void ScaledRootWithShapeFailsWithClearMessage()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Import(ScaledSubtreeModelBytes(rootScale: 2.0f)));
        Assert.Contains("no unit-scale node ancestor", ex.Message);
    }

    private static byte[] HierarchyModelBytes()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var zBufferType = builder.AddBlockType("NiZBufferProperty");

        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "root", children: [1], translation: (1, 0, 0)));
        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "child", children: [3], translation: (0, -2, 0), rotation: OarRotation));
        builder.AddBlock(zBufferType, NifTestProperties.ZBuffer(flags: 3, function: 3));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 4, properties: [2]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)],
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static byte[] ScaledSubtreeModelBytes(float rootScale = 1.0f)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var zBufferType = builder.AddBlockType("NiZBufferProperty");

        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "root", children: [1], scale: rootScale));
        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "scaled", children: [3], translation: (0, -1, 0), scale: 2.0f));
        builder.AddBlock(zBufferType, NifTestProperties.ZBuffer(flags: 3, function: 3));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 4, properties: [2]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)],
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    // The real R_OAR14 stored rest rotation from nwg1_galley.nifcache.
    private static readonly FileFormats.Nif.NifMatrix33 OarRotation = new(
        0.38575035f, -0.5062548f, -0.7712994f,
        -0.7839769f, -0.6206029f, 0.015251815f,
        -0.48639184f, 0.5987975f, -0.63628995f);

    private static readonly Vector3[] SourceVertices =
    [
        new(0, 0, 0),
        new(1, 0, 0),
        new(0, 1, 0),
        new(0, 0, 1),
    ];

    private static Matrix4x4 SourceToEngineMatrix()
    {
        // The source-to-engine map E: Rz(90) with meters-to-feet, the same
        // conversion NifTransformConverter applies inside its bake.
        var e = Matrix4x4.Identity;
        e.M11 = 0.0f;
        e.M12 = 1.0f;
        e.M21 = -1.0f;
        e.M22 = 0.0f;
        e.M11 *= NifTransformConverter.FeetPerMeter;
        e.M12 *= NifTransformConverter.FeetPerMeter;
        e.M21 *= NifTransformConverter.FeetPerMeter;
        e.M22 *= NifTransformConverter.FeetPerMeter;
        e.M33 *= NifTransformConverter.FeetPerMeter;
        return e;
    }

    private static Matrix4x4 BoneLocal(ImportedBone bone)
    {
        var local = Matrix4x4.CreateFromQuaternion(bone.BindRotation);
        local.M41 = bone.BindTranslation.X;
        local.M42 = bone.BindTranslation.Y;
        local.M43 = bone.BindTranslation.Z;
        return local;
    }

    private static void AssertMatricesEqual(Matrix4x4 expected, Matrix4x4 actual)
    {
        var expectedElements = new[]
        {
            expected.M11, expected.M12, expected.M13,
            expected.M21, expected.M22, expected.M23,
            expected.M31, expected.M32, expected.M33,
            expected.M41, expected.M42, expected.M43,
        };
        var actualElements = new[]
        {
            actual.M11, actual.M12, actual.M13,
            actual.M21, actual.M22, actual.M23,
            actual.M31, actual.M32, actual.M33,
            actual.M41, actual.M42, actual.M43,
        };
        for (var i = 0; i < expectedElements.Length; i++)
        {
            Assert.True(
                MathF.Abs(expectedElements[i] - actualElements[i]) < 1e-4f,
                $"Matrix element {i}: expected {expectedElements[i]}, got {actualElements[i]}.");
        }
    }

    [Fact]
    public void BakesSourceToEngineMatrixIntoPositions()
    {
        // One meter along source -Y (the forward axis) must land on +X at
        // 3.28084 world units; source +Z (up) stays up.
        var model = Import(StaticModelBytes());
        var mesh = (ImportedMesh)model.SubObjects[0].RenderObject;

        // Vertices were (0,0,0), (1,0,0), (0,1,0), (0,0,1):
        // engine = (-y, x, z) * feet.
        const float f = 3.280839895f;
        AssertEqual(new Vector3(0, 0, 0), mesh.Vertices[0].Position);
        AssertEqual(new Vector3(0, f, 0), mesh.Vertices[1].Position);
        AssertEqual(new Vector3(-f, 0, 0), mesh.Vertices[2].Position);
        AssertEqual(new Vector3(0, 0, f), mesh.Vertices[3].Position);
    }

    [Fact]
    public void BakesShapeTransformIntoPositionsAndNormals()
    {
        // Shape translated (0, -2, 0) in source space: forward by 2 meters
        // becomes +X by 2 * 3.28084.
        var model = Import(StaticModelBytes(shapeTranslation: (0, -2, 0)));
        var mesh = (ImportedMesh)model.SubObjects[0].RenderObject;

        const float f = 3.280839895f;
        AssertEqual(new Vector3(2 * f, 0, 0), mesh.Vertices[0].Position);
        // Normals were (0,0,1) and the bake is a pure rotation + uniform
        // scale, so they stay (0,0,1) after normalization.
        AssertEqual(new Vector3(0, 0, 1), mesh.Vertices[0].Normal);
    }

    [Fact]
    public void BoundsAreRecomputedFromBakedPositions()
    {
        var model = Import(StaticModelBytes());
        var mesh = (ImportedMesh)model.SubObjects[0].RenderObject;

        const float f = 3.280839895f;
        Assert.Equal(new Vector3(-f, 0, 0), mesh.BoundingBox.Min);
        Assert.Equal(new Vector3(0, f, f), mesh.BoundingBox.Max);
        Assert.True(mesh.BoundingSphere.Radius > 0);
    }

    [Fact]
    public void MaterialMapsFixedFunctionStateAndTextureName()
    {
        var model = Import(StaticModelBytes());
        var mesh = (ImportedMesh)model.SubObjects[0].RenderObject;

        Assert.False(mesh.IsSkinned);
        var material = Assert.Single(mesh.Materials);
        Assert.Equal(ImportedMaterial.FixedFunctionFamily, material.ShaderFamily);
        Assert.Equal(ImportedDepthComparison.LessEqual, material.DepthComparison);
        Assert.True(material.DepthWriteEnabled);
        Assert.False(material.BlendEnabled);

        var shading = material.FixedFunctionShading;
        Assert.NotNull(shading);
        Assert.Equal(new Vector3(0.4f, 0.5f, 0.6f), shading.Diffuse);
        Assert.Equal(new Vector3(0.25f, 0.25f, 0.25f), shading.Specular);
        Assert.Equal(0.1f, shading.Shininess, 3); // glossiness 10 / 100
        Assert.Equal(1.0f, shading.Opacity);
        Assert.Equal(0, shading.TextureStageCount);

        var textureProperty = Assert.Single(material.Properties);
        Assert.Equal("Texture0", textureProperty.Name);
        Assert.Equal("hull.tga", textureProperty.TextureName);
    }

    [Fact]
    public void MirrorScaleReversesWinding()
    {
        // A negative scale on the shape mirrors handedness; winding must be
        // reversed to keep front faces correct.
        var model = Import(StaticModelBytes(shapeScale: -1.0f));
        var mesh = (ImportedMesh)model.SubObjects[0].RenderObject;

        // Original first triangle (0, 1, 2) becomes (0, 2, 1).
        Assert.Equal(0, mesh.Indices[0]);
        Assert.Equal(2, mesh.Indices[1]);
        Assert.Equal(1, mesh.Indices[2]);
    }

    [Fact]
    public void HighestDetailLodChildIsBakedOthersOmitted()
    {
        var builder = new NifTestDataBuilder();
        var lodType = builder.AddBlockType("NiLODNode");
        var rangeType = builder.AddBlockType("NiRangeLODData");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");

        builder.AddBlock(lodType, NifTestLod.LodNode([1, 3], 5));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "detail_LOD0", data: 2));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(numVertices: 3, dataFlags: 0));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "detail_LOD1", data: 4));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(numVertices: 3, dataFlags: 0));
        builder.AddBlock(rangeType, NifTestLod.RangeLodData((0.0f, 3.0f), (3.0f, 100000.0f)));
        builder.AddTrailing(NifTestBlocks.Footer());

        var model = Import(builder.Build());

        Assert.NotNull(model);
        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal("detail_LOD0", subObject.DisplayName);
        Assert.Equal(0, subObject.ParentBoneIndex);
        var bone = Assert.Single(model.Skeleton.Bones);
        Assert.Equal(-1, bone.ParentIndex);
    }

    [Fact]
    public void EmptySceneFailsWithContext()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root"));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Import(builder.Build()));
        Assert.Contains("no convertible geometry", ex.Message);
    }

    private static void AssertEqual(Vector3 expected, Vector3 actual)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.X) < 1e-4f &&
            MathF.Abs(expected.Y - actual.Y) < 1e-4f &&
            MathF.Abs(expected.Z - actual.Z) < 1e-4f,
            $"Expected {expected}, got {actual}");
    }
}
