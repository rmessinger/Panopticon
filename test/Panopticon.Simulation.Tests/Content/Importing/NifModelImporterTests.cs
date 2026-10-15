using System;
using System.Collections.Generic;
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

    private static byte[] SkinnedModelBytes()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var materialType = builder.AddBlockType("NiMaterialProperty");
        var textureType = builder.AddBlockType("NiTexturingProperty");
        var sourceTextureType = builder.AddBlockType("NiSourceTexture");
        var zBufferType = builder.AddBlockType("NiZBufferProperty");
        var skinDataType = builder.AddBlockType("NiSkinData");
        var skinInstanceType = builder.AddBlockType("NiSkinInstance");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [4]));
        builder.AddBlock(zBufferType, NifTestProperties.ZBuffer(flags: 3, function: 3));
        builder.AddBlock(materialType, NifTestProperties.Material());
        builder.AddBlock(textureType, NifTestProperties.Texturing(baseSlot: (6, 3, 2, 0, 0, -100)));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 5, skin: 8));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(numVertices: 4));
        builder.AddBlock(sourceTextureType, NifTestProperties.SourceTexture(fileName: "hull.tga"));
        builder.AddBlock(skinDataType, NifTestBlocks.SkinData(
            boneWeights: [[(0, 1.0f), (1, 1.0f), (2, 1.0f), (3, 1.0f)]]));
        builder.AddBlock(skinInstanceType, NifTestBlocks.SkinInstance(
            data: 7, skeletonRoot: 0, bones: [0]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    [Fact]
    public void NormallessShapeImportsWithSynthesizedUnitNormals()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 2));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
            hasNormals: false,
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var model = Import(builder.Build(), "normalless")!;

        var mesh = (ImportedMesh)model!.SubObjects[0].RenderObject;
        Assert.All(mesh.Vertices, vertex =>
            Assert.True(
                MathF.Abs(vertex.Normal.Length() - 1f) < 1e-4f,
                $"Expected a unit normal, got {vertex.Normal}."));
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

        Assert.Equal(2, model!.Skeleton.Bones.Count);
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
        var mesh = (ImportedMesh)model!.SubObjects[0].RenderObject;
        var bake = childLocal * rootLocal * SourceToEngineMatrix();
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            var expected = Vector3.Transform(SourceVertices[i], bake);
            var actual = Vector3.Transform(mesh.Vertices[i].Position, actualWorld);
            AssertEqual(expected, actual);
        }
    }

    [Fact]
    public void ScaledSubtreeBecomesBoneWithBindScale()
    {
        var model = Import(ScaledSubtreeModelBytes());

        Assert.Equal(2, model!.Skeleton.Bones.Count);
        Assert.Equal("root", model.Skeleton.Bones[0].Name);
        var scaled = model.Skeleton.Bones[1];
        Assert.Equal("scaled", scaled.Name);
        Assert.Equal(0, scaled.ParentIndex);
        Assert.Equal(2f, scaled.BindScale, 5);
        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal(1, subObject.ParentBoneIndex);

        // Composing the scaled bone locals must equal the conjugated
        // source world chain, the rest-pose parity for scaled rests.
        var file = FileFormats.Nif.NifFile.Read(new MemoryStream(ScaledSubtreeModelBytes()));
        var rootLocal = NifTransformConverter.CreateLocalMatrix((FileFormats.Nif.NifAvObject)file.Blocks[0]);
        var scaledLocal = NifTransformConverter.CreateLocalMatrix((FileFormats.Nif.NifAvObject)file.Blocks[1]);
        var expectedWorld = NifTransformConverter.ConjugateSourceToEngine(scaledLocal * rootLocal);
        var actualWorld = BoneLocal(model.Skeleton.Bones[1]) * BoneLocal(model.Skeleton.Bones[0]);
        AssertMatricesEqual(expectedWorld, actualWorld);

        // Vertices are baked identity-relative to the scaled bone, so the
        // bone chain lands them on the C4a world bake.
        var mesh = (ImportedMesh)subObject.RenderObject;
        var bake = scaledLocal * rootLocal * SourceToEngineMatrix();
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            var expected = Vector3.Transform(SourceVertices[i], bake);
            var actual = Vector3.Transform(mesh.Vertices[i].Position, actualWorld);
            AssertEqual(expected, actual);
        }
    }

    [Fact]
    public void ScaledRootBecomesBoneAndShapeAttaches()
    {
        var model = Import(ScaledSubtreeModelBytes(rootScale: 2.0f));

        Assert.Equal(2, model!.Skeleton.Bones.Count);
        Assert.Equal(2f, model.Skeleton.Bones[0].BindScale, 5);
        Assert.Equal(2f, model.Skeleton.Bones[1].BindScale, 5);
        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal(1, subObject.ParentBoneIndex);

        // The shape bakes identity-relative to its direct parent bone:
        // (1, 0, 0) -> (0, 1, 0) * feet; the doubled ancestors act only
        // through the bone chain.
        var mesh = (ImportedMesh)subObject.RenderObject;
        const float f = 3.280839895f;
        AssertEqual(new Vector3(0, 1 * f, 0), mesh.Vertices[1].Position);
    }

    [Fact]
    public void SingularRootWithShapeFailsWithClearMessage()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Import(ScaledSubtreeModelBytes(rootScale: 0.0f)));
        Assert.Contains("no bone ancestor with a usable rest scale", ex.Message);
    }

    [Fact]
    public void SingularRestSubtreeStaysPlaceableWhenUnanimated()
    {
        var graph = ImportGraph(SingularSubtreeModelBytes());

        var bone = Assert.Single(graph.Skeleton.Bones);
        Assert.Equal("root", bone.Name);
        var subObject = Assert.Single(graph.SubObjects);
        Assert.Equal(0, subObject.ParentBoneIndex);
        Assert.True(graph.CollapsedScaleNodes.TryGetValue("boom", out var scale));
        Assert.Equal(0.0f, scale);

        // The unanimated shape keeps its C4c placement: identity bake, v * E.
        var mesh = (ImportedMesh)subObject.RenderObject;
        AssertEqual(new Vector3(0, 3.280839895f, 0), mesh.Vertices[1].Position);
    }

    [Fact]
    public void NonFiniteRestScaleCollapsesInsteadOfThrowing()
    {
        var graph = ImportGraph(SingularSubtreeModelBytes(boomScale: float.NaN));

        Assert.Single(graph.Skeleton.Bones);
        Assert.True(graph.CollapsedScaleNodes.TryGetValue("boom", out var scale));
        Assert.True(float.IsNaN(scale));
    }

    [Fact]
    public void SkinnedShapeImportsAsSkinnedMeshOnRootBone()
    {
        // Skinned-shape conversion landed with WI-C4d-06; the identity
        // skin data of this fixture yields the identity inverse bind.
        var model = Import(SkinnedModelBytes());

        Assert.NotNull(model);
        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal(0, subObject.ParentBoneIndex);
        Assert.True(subObject.RenderObject is ImportedMesh { IsSkinned: true });
        var bone = Assert.Single(model.Skeleton.Bones);
        Assert.Equal(1f, bone.InverseBindMatrix.M11);
        Assert.Equal(0f, bone.InverseBindMatrix.M41);
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

    private static byte[] SingularSubtreeModelBytes(float boomScale = 0.0f)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1, 2]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "boom", scale: boomScale));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 3));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 3,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0)]));
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
        // BindScale multiplies the linear part only; the translation row
        // is already in engine units.
        local.M11 *= bone.BindScale;
        local.M12 *= bone.BindScale;
        local.M13 *= bone.BindScale;
        local.M21 *= bone.BindScale;
        local.M22 *= bone.BindScale;
        local.M23 *= bone.BindScale;
        local.M31 *= bone.BindScale;
        local.M32 *= bone.BindScale;
        local.M33 *= bone.BindScale;
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
    public void StoredColumnVectorRotationMapsChildPlusYToPlusZ()
    {
        // The stored NIF 3x3 is the column-vector map: rows
        // [[1,0,0],[0,0,-1],[0,1,0]] rotate +90 degrees about X, so a
        // child at source +Y lands at source +Z once the matrix is
        // transposed into the row-vector engine layout. A positional copy
        // (the pre-C4d bug) would map +Y to -Z.
        var stored = new FileFormats.Nif.NifMatrix33(
            1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, -1.0f,
            0.0f, 1.0f, 0.0f);

        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", rotation: stored));
        builder.AddTrailing(NifTestBlocks.Footer());
        var file = FileFormats.Nif.NifFile.Read(new MemoryStream(builder.Build()));

        var local = NifTransformConverter.CreateLocalMatrix((FileFormats.Nif.NifAvObject)file.Blocks[0]);
        AssertEqual(new Vector3(0, 0, 1), Vector3.Transform(new Vector3(0, 1, 0), local));
    }

    [Fact]
    public void BakesSourceToEngineMatrixIntoPositions()
    {
        // One meter along source -Y (the forward axis) must land on +X at
        // 3.28084 world units; source +Z (up) stays up.
        var model = Import(StaticModelBytes());
        var mesh = (ImportedMesh)model!.SubObjects[0].RenderObject;

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
        var mesh = (ImportedMesh)model!.SubObjects[0].RenderObject;

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
        var mesh = (ImportedMesh)model!.SubObjects[0].RenderObject;

        const float f = 3.280839895f;
        Assert.Equal(new Vector3(-f, 0, 0), mesh.BoundingBox.Min);
        Assert.Equal(new Vector3(0, f, f), mesh.BoundingBox.Max);
        Assert.True(mesh.BoundingSphere.Radius > 0);
    }

    [Fact]
    public void MaterialMapsFixedFunctionStateAndTextureName()
    {
        var model = Import(StaticModelBytes());
        var mesh = (ImportedMesh)model!.SubObjects[0].RenderObject;

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
        var mesh = (ImportedMesh)model!.SubObjects[0].RenderObject;

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

    private static NifSceneGraph ImportGraph(
        byte[] fileBytes,
        string name = "test_model")
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile($"cache/nifcache/{name}.nifcache", fileBytes);
        var context = new AssetImportContext(fileSystem, language: null);
        var file = NifModelImporter.ReadNifFile(name, context);
        Assert.NotNull(file);
        return NifSceneConverter.ConvertGraph(file!, name, context);
    }

    private static byte[] NodeTargetModelBytes()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var materialType = builder.AddBlockType("NiMaterialProperty");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1, 2]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "Object04"));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(
            name: "hull", data: 4, properties: [3]));
        builder.AddBlock(materialType, NifTestProperties.Material());
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 3,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static byte[] TwinShapeModelBytes(params string[] shapeNames)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var materialType = builder.AddBlockType("NiMaterialProperty");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [2, 3]));
        builder.AddBlock(materialType, NifTestProperties.Material());
        builder.AddBlock(shapeType, NifTestGeometry.Shape(
            name: shapeNames[0], data: 4, properties: [1]));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(
            name: shapeNames[1], data: 5, properties: [1]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 3,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0)]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 3,
            vertices: [(0, 0, 0), (0, 1, 0), (0, 0, 1)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    [Fact]
    public void PropertyTargetMapResolvesNodeVisibilityTarget()
    {
        var graph = ImportGraph(NodeTargetModelBytes());

        Assert.True(graph.PropertyTargets.TryGetBoneName(1, out var boneName));
        Assert.Equal("Object04", boneName);
        Assert.True(graph.PropertyTargets.TryGetBoneName(0, out var rootName));
        Assert.Equal("root", rootName);
    }

    [Fact]
    public void PropertyTargetMapResolvesShapeVisibilityTarget()
    {
        var graph = ImportGraph(StaticModelBytes());

        Assert.True(graph.PropertyTargets.TryGetSubObjectFullName(4, out var fullName));
        Assert.Equal("test_model.0.hull", fullName);
        Assert.False(graph.PropertyTargets.TryGetSubObjectFullName(0, out _));
    }

    [Fact]
    public void TexturingPropertyTargetResolvesByStringRefAndEmbeddedRef()
    {
        var graph = ImportGraph(StaticModelBytes());

        Assert.True(NifPropertyTargetMap.TryParsePropertyTarget(
            "hull\nPROP\nNiTexturingProperty", out var shapeName, out var propertyTypeName));
        Assert.Equal("hull", shapeName);
        Assert.Equal("NiTexturingProperty", propertyTypeName);
        Assert.False(NifPropertyTargetMap.TryParsePropertyTarget("Object04", out _, out _));

        var byName = graph.PropertyTargets.ResolvePropertySlots(shapeName, propertyTypeName);
        var byReference = graph.PropertyTargets.ResolvePropertySlots(3);
        var slot = Assert.Single(byName);
        Assert.Equal(slot, Assert.Single(byReference));
        Assert.Equal("test_model.0.hull", slot.SubObjectFullName);
        Assert.Equal(0, slot.MaterialIndex);
    }

    [Fact]
    public void SharedMaterialPropertyFansOutToBothShapes()
    {
        var graph = ImportGraph(TwinShapeModelBytes("canopy", "fuselage"));

        var byReference = graph.PropertyTargets.ResolvePropertySlots(1);
        Assert.Equal(2, byReference.Count);
        Assert.Equal("test_model.0.canopy", byReference[0].SubObjectFullName);
        Assert.Equal("test_model.1.fuselage", byReference[1].SubObjectFullName);
        Assert.Equal(0, byReference[0].MaterialIndex);

        var byName = graph.PropertyTargets.ResolvePropertySlots("canopy", "NiMaterialProperty");
        Assert.Equal(byReference, byName);
    }

    [Fact]
    public void DuplicateShapeNamesMakePropertyTargetsAmbiguous()
    {
        var graph = ImportGraph(TwinShapeModelBytes("wing", "wing"));

        var ex = Assert.Throws<InvalidDataException>(
            () => graph.PropertyTargets.ResolvePropertySlots("wing", "NiMaterialProperty"));
        Assert.Contains("2 shapes named 'wing'", ex.Message);
        Assert.Contains("ambiguous", ex.Message);
    }

    [Fact]
    public void MissingPropertyTargetFailsWithModelContext()
    {
        var graph = ImportGraph(StaticModelBytes());

        var missingShape = Assert.Throws<InvalidDataException>(
            () => graph.PropertyTargets.ResolvePropertySlots("nope", "NiMaterialProperty"));
        Assert.Contains("test_model", missingShape.Message);
        Assert.Contains("no converted shape named 'nope'", missingShape.Message);

        var unknownProperty = Assert.Throws<InvalidDataException>(
            () => graph.PropertyTargets.ResolvePropertySlots(99));
        Assert.Contains("test_model", unknownProperty.Message);

        var unusedPropertyType = Assert.Throws<InvalidDataException>(
            () => graph.PropertyTargets.ResolvePropertySlots("hull", "NiAlphaProperty"));
        Assert.Contains("has no NiAlphaProperty to animate", unusedPropertyType.Message);
    }

    private static byte[] MorphModelBytes(
        byte relativeTargets = 1,
        bool baseOnly = false,
        bool mismatchBase = false,
        uint morphVertexCount = 4,
        bool skinned = false,
        bool embedded = true)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var morpherType = builder.AddBlockType("NiGeomMorpherController");
        var morphDataType = builder.AddBlockType("NiMorphData");
        var skinDataType = builder.AddBlockType("NiSkinData");
        var skinInstanceType = builder.AddBlockType("NiSkinInstance");

        var vertices = new (float, float, float)[] { (0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1) };
        var baseVectors = mismatchBase
            ? new (float, float, float)[] { (0, 0, 0), (1, 0, 0), (0, 1, 0), (9, 9, 9) }
            : vertices;

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(
            name: "Editable Mesh",
            data: 2,
            skin: skinned ? 6 : -1,
            controller: embedded ? 3 : -1));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: vertices,
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)]));
        builder.AddBlock(morpherType, NifTestPropertyAnimation.MorphController(data: 4));

        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)> zeroKeys =
            [(0f, 0f, null, null), (1f, 0f, null, null)];
        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)> oneKeys =
            [(0f, 0f, null, null), (1f, 1f, null, null)];
        IReadOnlyList<(float X, float Y, float Z)> deltaVectors =
            [(1f, 0f, 0f), (0f, 2f, 0f), (0f, 0f, 3f), (0.5f, 0.5f, 0.5f)];

        var morphs = new List<(
            IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)>? WeightKeys,
            IReadOnlyList<(float X, float Y, float Z)>? Vectors)>
        {
            (zeroKeys, baseVectors),
        };
        if (!baseOnly)
        {
            morphs.Add((oneKeys, deltaVectors));
        }

        builder.AddBlock(morphDataType, NifTestPropertyAnimation.MorphData(
            numVertices: morphVertexCount,
            morphs: morphs,
            relativeTargets: relativeTargets));

        if (skinned)
        {
            builder.AddBlock(skinDataType, NifTestBlocks.SkinData(
                boneWeights: [[(0, 1.0f), (1, 1.0f), (2, 1.0f), (3, 1.0f)]]));
            builder.AddBlock(skinInstanceType, NifTestBlocks.SkinInstance(
                data: 5, skeletonRoot: 0, bones: [0]));
        }

        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static byte[] MorphKfBytes(
        string targetName = "Editable Mesh",
        bool keyframeOnly = false,
        bool duplicateTargets = false)
    {
        var builder = new NifTestDataBuilder();
        var sequenceType = builder.AddBlockType("NiControllerSequence");
        var morpherType = builder.AddBlockType("NiGeomMorpherController");
        var morphDataType = builder.AddBlockType("NiMorphData");
        var keyframeControllerType = builder.AddBlockType("NiKeyframeController");
        var keyframeDataType = builder.AddBlockType("NiKeyframeData");

        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)> zeroKey =
            [(0f, 0f, null, null)];
        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)> weightKeys =
            [(0f, 0f, null, null), (1f, 1f, null, null)];
        IReadOnlyList<(float X, float Y, float Z)> baseVectors =
            [(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, 1f)];
        IReadOnlyList<(float X, float Y, float Z)> deltaVectors =
            [(1f, 0f, 0f), (0f, 2f, 0f), (0f, 0f, 3f), (0.5f, 0.5f, 0.5f)];

        if (keyframeOnly)
        {
            builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
                "EMPTY_SEQUENCE", "root", textKeys: -1, ("root", 1)));
            builder.AddBlock(keyframeControllerType, NifTestKeyframe.KeyframeController(data: 2));
            builder.AddBlock(keyframeDataType, NifTestKeyframe.KeyframeData());
        }
        else if (duplicateTargets)
        {
            builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
                "EMPTY_SEQUENCE", "root", textKeys: -1, (targetName, 1), (targetName, 2)));
            builder.AddBlock(morpherType, NifTestPropertyAnimation.MorphController(data: 3));
            builder.AddBlock(morpherType, NifTestPropertyAnimation.MorphController(data: 3));
            builder.AddBlock(morphDataType, NifTestPropertyAnimation.MorphData(
                numVertices: 4,
                morphs: [(zeroKey, baseVectors), (weightKeys, deltaVectors)]));
        }
        else
        {
            builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
                "EMPTY_SEQUENCE", "root", textKeys: -1, (targetName, 1)));
            builder.AddBlock(morpherType, NifTestPropertyAnimation.MorphController(data: 2));
            builder.AddBlock(morphDataType, NifTestPropertyAnimation.MorphData(
                numVertices: 4,
                morphs: [(zeroKey, baseVectors), (weightKeys, deltaVectors)]));
        }

        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static ImportedModel? ImportMorph(
        byte[] fileBytes,
        string name = "test_model",
        bool gate = true,
        byte[]? kfBytes = null,
        string kfPath = "Graphics/Building/test.kf")
    {
        var importer = new NifModelImporter();
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile($"cache/nifcache/{name}.nifcache", fileBytes);
        if (kfBytes is not null)
        {
            fileSystem.AddFile(kfPath, kfBytes);
        }

        var key = kfBytes is null ? name : $"{name}#morph-kf={kfPath}";
        return importer.ImportModel(
            key,
            new AssetImportContext(
                fileSystem,
                language: null,
                gate ? [ImportedFeatureGates.MorphTargets] : null));
    }

    private static ImportedMesh MorphMesh(ImportedModel model)
    {
        return Assert.IsType<ImportedMesh>(model.SubObjects[0].RenderObject);
    }

    [Fact]
    public void EmbeddedMorphControllerAttachesTargetSetWithConvertedDeltas()
    {
        var model = ImportMorph(MorphModelBytes());
        Assert.NotNull(model);

        var mesh = MorphMesh(model!);
        Assert.Equal("test_model.0.Editable Mesh", mesh.PrimitiveId);

        var set = Assert.Single(mesh.MorphTargetSets);
        Assert.Equal("test_model.0.Editable Mesh", set.PrimitiveId);
        Assert.Equal("Target1", Assert.Single(set.TargetNames));
        Assert.Equal(0.0f, Assert.Single(set.DefaultWeights));
        Assert.Equal(new ImportedMorphWeightRange(0.0f, 1.0f), Assert.Single(set.AllowedWeightRanges));

        var sourceDeltas = new Vector3[]
        {
            new(1, 0, 0),
            new(0, 2, 0),
            new(0, 0, 3),
            new(0.5f, 0.5f, 0.5f),
        };
        var engine = SourceToEngineMatrix();
        var deltas = Assert.Single(set.Targets).PositionDeltas!;
        Assert.Equal(4, deltas.Count);
        for (var i = 0; i < sourceDeltas.Length; i++)
        {
            AssertEqual(Vector3.Transform(sourceDeltas[i], engine), deltas[i]);
        }
    }

    [Fact]
    public void BaseOnlyMorphDataYieldsNoTargetSet()
    {
        var model = ImportMorph(MorphModelBytes(baseOnly: true));
        Assert.NotNull(model);

        var mesh = MorphMesh(model!);
        Assert.Equal(string.Empty, mesh.PrimitiveId);
        Assert.Empty(mesh.MorphTargetSets);
    }

    [Fact]
    public void Morph0MismatchWithBasePositionsFails()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => ImportMorph(MorphModelBytes(mismatchBase: true)));
        Assert.Contains("morph 0", ex.Message);
        Assert.Contains("vertex 3", ex.Message);
    }

    [Fact]
    public void AbsoluteMorphTargetsRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ImportMorph(MorphModelBytes(relativeTargets: 0)));
        Assert.Contains("RelativeTargets=0", ex.Message);
    }

    [Fact]
    public void MorphVertexCountMismatchFails()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => ImportMorph(MorphModelBytes(morphVertexCount: 3)));
        Assert.Contains("3 morph vectors", ex.Message);
        Assert.Contains("4 vertices", ex.Message);
    }

    [Fact]
    public void MorphModelWithoutGateFailsNamingGate()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ImportMorph(MorphModelBytes(), gate: false));
        Assert.Contains("MorphTargets", ex.Message);
    }

    [Fact]
    public void SkinnedShapeWithMorphFails()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ImportMorph(MorphModelBytes(skinned: true)));
        Assert.Contains("combines skinning with morph targets", ex.Message);
    }

    [Fact]
    public void MorphKfPairingAttachesTargetSetByShapeName()
    {
        var model = ImportMorph(
            MorphModelBytes(embedded: false),
            kfBytes: MorphKfBytes());
        Assert.NotNull(model);

        var mesh = MorphMesh(model!);
        var set = Assert.Single(mesh.MorphTargetSets);
        Assert.Equal("Target1", Assert.Single(set.TargetNames));
        Assert.Equal(4, Assert.Single(set.Targets).PositionDeltas!.Count);
    }

    [Fact]
    public void UnpairedMorphKfReturnsNull()
    {
        var model = ImportMorph(
            MorphModelBytes(embedded: false),
            kfBytes: MorphKfBytes(keyframeOnly: true));
        Assert.Null(model);
    }

    [Fact]
    public void MissingKfEntryReturnsNull()
    {
        var importer = new NifModelImporter();
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("cache/nifcache/test_model.nifcache", MorphModelBytes());

        var model = importer.ImportModel(
            "test_model#morph-kf=Graphics/Building/absent.kf",
            new AssetImportContext(fileSystem, language: null, [ImportedFeatureGates.MorphTargets]));
        Assert.Null(model);
    }

    [Fact]
    public void MorphKfTargetingMissingShapeFails()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => ImportMorph(MorphModelBytes(embedded: false), kfBytes: MorphKfBytes(targetName: "nope")));
        Assert.Contains("'nope'", ex.Message);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void DuplicateMorphTargetsInKfRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ImportMorph(MorphModelBytes(embedded: false), kfBytes: MorphKfBytes(duplicateTargets: true)));
        Assert.Contains("multiple morph controllers", ex.Message);
    }

    [Fact]
    public void EmbeddedAndPairedMorphSourcesRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ImportMorph(MorphModelBytes(), kfBytes: MorphKfBytes()));
        Assert.Contains("both an embedded morpher controller and a paired morph KF source", ex.Message);
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
