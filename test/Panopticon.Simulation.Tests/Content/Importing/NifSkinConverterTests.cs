using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.IO;
using Panopticon.Tests.Data.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;

namespace Panopticon.Tests.Content.Importing;

public sealed class NifSkinConverterTests
{
    private const float Tolerance = 1e-4f;

    private static readonly (float X, float Y, float Z)[] DefaultVertices =
    [
        (0, 0, 0),
        (1, 0, 0),
        (0, 1, 0),
        (0, 0, 1),
    ];

    private static readonly IReadOnlyList<(ushort VertexIndex, float Weight)[]> DefaultBoneWeights =
    [
        // arm
        [(0, 1.0f), (1, 0.3f), (2, 1.0f)],
        // leg
        [(1, 0.7f), (3, 1.0f)],
    ];

    private static ImportedModel? Import(
        byte[] fileBytes,
        string name = "test_skin")
    {
        var importer = new NifModelImporter();
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile($"cache/nifcache/{name}.nifcache", fileBytes);
        return importer.ImportModel(name, new AssetImportContext(fileSystem, language: null));
    }

    /// <summary>
    /// Two bones posed differently from their binds (rest != bind), skin
    /// transform T(0.5, 0, 0), arm bind T(2, 0, 0), leg bind T(0, 5, 0).
    /// Blocks: 0 root, 1 arm T(1,0,0), 2 leg T(0,1,0), 3 shape, 4 data,
    /// 5 skin data, 6 skin instance, then any extra nodes.
    /// </summary>
    private static byte[] TwoBoneSkinBytes(
        IReadOnlyList<(ushort VertexIndex, float Weight)[]>? boneWeights = null,
        IReadOnlyList<(float X, float Y, float Z)>? boneTranslations = null,
        (float X, float Y, float Z)? skinTranslation = null,
        byte hasVertexWeights = 1,
        int skeletonRoot = 0,
        float armScale = 1.0f,
        int extraNodes = 0,
        IReadOnlyList<(float X, float Y, float Z)>? vertices = null,
        IReadOnlyList<(float X, float Y, float Z)>? normals = null)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var skinDataType = builder.AddBlockType("NiSkinData");
        var skinInstanceType = builder.AddBlockType("NiSkinInstance");

        var sourceVertices = vertices ?? DefaultVertices;
        var sourceNormals = normals ??
        [
            (0.0f, 0.0f, 1.0f),
            (0.0f, 0.0f, 1.0f),
            (0.0f, 0.0f, 1.0f),
            (0.0f, 0.0f, 1.0f),
        ];

        var children = new List<int> { 1, 2, 3 };
        for (var i = 0; i < extraNodes; i++)
        {
            children.Add(7 + i);
        }

        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "root", children: [.. children]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "arm", translation: (1, 0, 0), scale: armScale));
        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "leg", translation: (0, 1, 0)));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(
            name: "body", data: 4, skin: 6));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [.. sourceVertices],
            normals: [.. sourceNormals],
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddBlock(skinDataType, NifTestBlocks.SkinData(
            boneWeights: boneWeights ?? DefaultBoneWeights,
            skinTranslation: skinTranslation ?? (0.5f, 0, 0),
            hasVertexWeights: hasVertexWeights,
            boneTranslations: boneTranslations ?? [(2, 0, 0), (0, 5, 0)]));
        builder.AddBlock(skinInstanceType, NifTestBlocks.SkinInstance(
            data: 5, skeletonRoot: skeletonRoot, bones: [1, 2]));
        for (var i = 0; i < extraNodes; i++)
        {
            builder.AddBlock(nodeType, NifTestBlocks.Node(
                name: $"extra{i}", translation: (0, 0, 0)));
        }

        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static ImportedModel ImportRequired(byte[] bytes)
    {
        var model = Import(bytes);
        Assert.NotNull(model);
        return model;
    }

    private static ImportedMesh MeshOf(ImportedModel model, int index = 0)
    {
        var mesh = Assert.IsType<ImportedMesh>(model.SubObjects[index].RenderObject);
        return mesh;
    }

    private static Vector3 ToEngine(Vector3 source)
    {
        return Vector3.Transform(source, SourceToEngineMatrix());
    }

    private static Matrix4x4 SourceToEngineMatrix()
    {
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

    private static void AssertEqual(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    [Fact]
    public void TwoBoneSkinBindsModelSpacePositions()
    {
        var model = ImportRequired(TwoBoneSkinBytes());

        var bones = model.Skeleton.Bones;
        Assert.Equal(3, bones.Count);
        Assert.Equal(["root", "arm", "leg"], [bones[0].Name, bones[1].Name, bones[2].Name]);
        var mesh = MeshOf(model);
        Assert.True(mesh.IsSkinned);
        Assert.Equal(0, model.SubObjects[0].ParentBoneIndex);

        // Positions: skin space minus S's translation, then source-to-engine.
        for (var i = 0; i < 4; i++)
        {
            var expected = ToEngine(new Vector3(
                DefaultVertices[i].X - 0.5f,
                DefaultVertices[i].Y,
                DefaultVertices[i].Z));
            AssertEqual(expected, mesh.Vertices[i].Position);
            AssertEqual(expected, mesh.Vertices[i].SecondaryPosition!.Value);
            AssertEqual(new Vector3(0, 0, 1), mesh.Vertices[i].Normal);
        }
    }

    [Fact]
    public void InfluencesSortDescendingAcrossBones()
    {
        var model = ImportRequired(TwoBoneSkinBytes());
        var mesh = MeshOf(model);

        // v1 blends leg 0.7 + arm 0.3, so slot 0 is leg (bone 2).
        Assert.Equal(2u, mesh.Vertices[1].BoneIndex0);
        Assert.Equal(1u, mesh.Vertices[1].BoneIndex1);
        Assert.Equal(0.7f, mesh.Vertices[1].BoneWeight0, 4);
        Assert.Equal(0.3f, mesh.Vertices[1].BoneWeight1, 4);
        Assert.Equal(0u, mesh.Vertices[1].BoneIndex2);
        Assert.Equal(0f, mesh.Vertices[1].BoneWeight2);

        // v0 is arm-only: slot 0 arm, unused slots bone 0 weight 0.
        Assert.Equal(1u, mesh.Vertices[0].BoneIndex0);
        Assert.Equal(1.0f, mesh.Vertices[0].BoneWeight0, 4);
        Assert.Equal(0u, mesh.Vertices[0].BoneIndex1);
        Assert.Equal(0f, mesh.Vertices[0].BoneWeight1);
    }

    [Fact]
    public void WeightsAtOrBelowToleranceAreDropped()
    {
        var model = ImportRequired(TwoBoneSkinBytes(boneWeights:
        [
            [(0, 1.0f), (2, 1.0f), (3, 1.0f)],
            [(0, 0.00005f), (1, 1.0f)],
        ]));
        var mesh = MeshOf(model);

        // The 5e-5 leg influence on v0 is dropped, not slotted.
        Assert.Equal(1u, mesh.Vertices[0].BoneIndex0);
        Assert.Equal(1.0f, mesh.Vertices[0].BoneWeight0, 4);
        Assert.Equal(0u, mesh.Vertices[0].BoneIndex1);
        Assert.Equal(0f, mesh.Vertices[0].BoneWeight1);
    }

    [Fact]
    public void InverseBindsAreSkinTimesBoneConjugatedToEngine()
    {
        var model = ImportRequired(TwoBoneSkinBytes());
        var bones = model.Skeleton.Bones;

        // Source binds S*B: arm T(2.5, 0, 0), leg T(0.5, 5, 0),
        // conjugated to engine as pure translations.
        var arm = bones[1].InverseBindMatrix;
        Assert.Equal(1.0f, arm.M11, Tolerance);
        var armTranslation = ToEngine(new Vector3(2.5f, 0, 0));
        AssertEqual(armTranslation, new Vector3(arm.M41, arm.M42, arm.M43));

        var leg = bones[2].InverseBindMatrix;
        Assert.Equal(1.0f, leg.M22, Tolerance);
        var legTranslation = ToEngine(new Vector3(0.5f, 5.0f, 0));
        AssertEqual(legTranslation, new Vector3(leg.M41, leg.M42, leg.M43));

        // The root bone carries no weights, so it keeps the identity.
        Assert.Equal(Matrix4x4.Identity, bones[0].InverseBindMatrix);
    }

    [Fact]
    public void BindPoseParityReproducesImportedPositions()
    {
        var model = ImportRequired(TwoBoneSkinBytes());
        var mesh = MeshOf(model);
        var bones = model.Skeleton.Bones;

        // Posing each bone at the inverse of its inverse bind makes every
        // skinning matrix (IB * relative) the identity, so the weighted
        // blend must reproduce the imported positions exactly.
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            var vertex = mesh.Vertices[i];
            var slots = new (uint Index, float Weight)[]
            {
                (vertex.BoneIndex0, vertex.BoneWeight0),
                (vertex.BoneIndex1, vertex.BoneWeight1),
                (vertex.BoneIndex2, vertex.BoneWeight2),
                (vertex.BoneIndex3, vertex.BoneWeight3),
            };

            var deformed = Vector3.Zero;
            foreach (var (index, weight) in slots)
            {
                if (weight == 0f)
                {
                    continue;
                }

                var bind = bones[(int)index].InverseBindMatrix;
                Matrix4x4.Invert(bind, out var relative);
                deformed += Vector3.Transform(vertex.Position, bind * relative) * weight;
            }

            AssertEqual(vertex.Position, deformed);
        }
    }

    [Fact]
    public void PosedBoneDeformsVertexPerWeight()
    {
        var model = ImportRequired(TwoBoneSkinBytes());
        var mesh = MeshOf(model);
        var bones = model.Skeleton.Bones;

        // v1 blends arm 0.3 / leg 0.7. Posing only the arm at its bind
        // plus delta shifts v1 by exactly 0.3 * delta; the leg stays at
        // bind so its skinning matrix is the identity.
        var armBind = bones[1].InverseBindMatrix;
        var delta = new Vector3(0.25f, 0, 0);
        Matrix4x4.Invert(armBind, out var armBindInverse);
        var armRelative = armBindInverse * Matrix4x4.CreateTranslation(delta);
        var armSkinning = armBind * armRelative;

        var legBind = bones[2].InverseBindMatrix;
        Matrix4x4.Invert(legBind, out var legRelative);
        var legSkinning = legBind * legRelative;

        var vertex = mesh.Vertices[1];
        var deformed = Vector3.Transform(vertex.Position, armSkinning) * 0.3f
            + Vector3.Transform(vertex.Position, legSkinning) * 0.7f;
        AssertEqual(vertex.Position + delta * 0.3f, deformed);
    }

    [Fact]
    public void SkinDataWithoutVertexWeightsFails()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Import(TwoBoneSkinBytes(hasVertexWeights: 0)));
        Assert.Contains("without vertex weights", ex.Message);
    }

    [Fact]
    public void SkeletonRootMustBeModelRoot()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Import(TwoBoneSkinBytes(skeletonRoot: 1)));
        Assert.Contains("only the model root", ex.Message);
    }

    [Fact]
    public void ScaledSkinBoneNamesC4f()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Import(TwoBoneSkinBytes(armScale: 2.0f)));
        Assert.Contains("'arm'", ex.Message);
        Assert.Contains("not a runtime bone", ex.Message);
        Assert.Contains("C4f", ex.Message);
    }

    [Fact]
    public void VertexIndexOutOfRangeFails()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Import(TwoBoneSkinBytes(boneWeights:
        [
            [(99, 1.0f)],
            [],
        ])));
        Assert.Contains("vertex 99", ex.Message);
    }

    [Fact]
    public void RepeatedVertexInOneBoneFails()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Import(TwoBoneSkinBytes(boneWeights:
        [
            [(0, 0.5f), (0, 0.5f)],
            [],
        ])));
        Assert.Contains("more than once", ex.Message);
    }

    [Fact]
    public void WeightAboveOneFails()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Import(TwoBoneSkinBytes(boneWeights:
        [
            [(0, 1.5f)],
            [],
        ])));
        Assert.Contains("outside [0, 1]", ex.Message);
    }

    [Fact]
    public void NonFiniteWeightFails()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Import(TwoBoneSkinBytes(boneWeights:
        [
            [(0, float.NaN)],
            [],
        ])));
        Assert.Contains("outside [0, 1]", ex.Message);
    }

    [Fact]
    public void FiveInfluencesAreRejected()
    {
        // Five bones weight v0 with 0.2 each: sum is 1, count is 5.
        var boneWeights = new (ushort VertexIndex, float Weight)[][]
        {
            [(0, 0.2f)],
            [(0, 0.2f)],
            [(0, 0.2f)],
            [(0, 0.2f)],
            [(0, 0.2f)],
        };

        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var skinDataType = builder.AddBlockType("NiSkinData");
        var skinInstanceType = builder.AddBlockType("NiSkinInstance");

        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "root", children: [1, 2, 3, 7, 8, 9]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "b0", translation: (0, 0, 0)));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "b1", translation: (0, 0, 0)));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "body", data: 4, skin: 6));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [.. DefaultVertices],
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)],
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddBlock(skinDataType, NifTestBlocks.SkinData(
            boneWeights: boneWeights,
            skinTranslation: (0, 0, 0),
            boneTranslations: [(0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0)]));
        builder.AddBlock(skinInstanceType, NifTestBlocks.SkinInstance(
            data: 5, skeletonRoot: 0, bones: [1, 2, 7, 8, 9]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "b2", translation: (0, 0, 0)));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "b3", translation: (0, 0, 0)));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "b4", translation: (0, 0, 0)));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<NotSupportedException>(() => Import(builder.Build()));
        Assert.Contains("vertex 0", ex.Message);
        Assert.Contains("5 meaningful skin influences", ex.Message);
    }

    [Fact]
    public void WeightSumFarFromOneFails()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Import(TwoBoneSkinBytes(boneWeights:
        [
            [(0, 0.842f)],
            [],
        ])));
        Assert.Contains("summing to 0.842", ex.Message);
        Assert.Contains("more than 0.01 from 1", ex.Message);
    }

    [Fact]
    public void UnweightedVertexFailsTheSumCheck()
    {
        // v1 is weighted, v0 gets nothing from any bone.
        var ex = Assert.Throws<InvalidDataException>(() => Import(TwoBoneSkinBytes(boneWeights:
        [
            [(1, 1.0f)],
            [],
        ])));
        Assert.Contains("vertex 0", ex.Message);
        Assert.Contains("more than 0.01 from 1", ex.Message);
    }

    [Fact]
    public void SharedBoneWithConflictingBindFails()
    {
        // Two skinned shapes share arm+leg; the arm bind differs.
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var skinDataType = builder.AddBlockType("NiSkinData");
        var skinInstanceType = builder.AddBlockType("NiSkinInstance");

        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "root", children: [1, 2, 3, 4]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "arm", translation: (1, 0, 0)));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "leg", translation: (0, 1, 0)));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "torso", data: 5, skin: 8));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "head", data: 6, skin: 10));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 2,
            vertices: [(0, 0, 0), (1, 0, 0)],
            normals: [(0, 0, 1), (0, 0, 1)],
            triangles: [(0, 1, 0)]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 2,
            vertices: [(0, 1, 0), (1, 1, 0)],
            normals: [(0, 0, 1), (0, 0, 1)],
            triangles: [(0, 1, 0)]));
        builder.AddBlock(skinDataType, NifTestBlocks.SkinData(
            boneWeights: [[(0, 1.0f)], [(1, 1.0f)]],
            boneTranslations: [(2, 0, 0), (0, 5, 0)]));
        builder.AddBlock(skinInstanceType, NifTestBlocks.SkinInstance(
            data: 7, skeletonRoot: 0, bones: [1, 2]));
        builder.AddBlock(skinDataType, NifTestBlocks.SkinData(
            boneWeights: [[(0, 1.0f)], [(1, 1.0f)]],
            boneTranslations: [(3, 0, 0), (0, 5, 0)]));
        builder.AddBlock(skinInstanceType, NifTestBlocks.SkinInstance(
            data: 9, skeletonRoot: 0, bones: [1, 2]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Import(builder.Build()));
        Assert.Contains("conflicting inverse bind", ex.Message);
        Assert.Contains("'arm'", ex.Message);
    }
}
