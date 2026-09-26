#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using FileFormatsNif = Panopticon.FileFormats.Nif;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Converts skinned NIF shapes (NiTriShape with a NiSkinInstance) into
/// imported meshes. Vertex positions are stored in skin space: the skin
/// transform S maps skeleton-root-parent space to skin space and each
/// NiSkinData bone transform B maps skin space to bind-pose bone space,
/// so a vertex becomes S^-1 * source-to-engine and the engine inverse
/// bind is the conjugate of S * B. Skinned meshes attach to bone 0 (the
/// model root) with IsSkinned; the shape's own node transform is ignored.
/// </summary>
internal static class NifSkinConverter
{
    private const float WeightTolerance = 1e-4f;
    private const float WeightSumTolerance = 1e-2f;
    private const float BindTolerance = 1e-4f;

    internal static void ConvertSkinnedShapes(
        FileFormatsNif.NifFile file,
        string modelName,
        IReadOnlyList<(int BlockIndex, FileFormatsNif.NifTriShape Shape)> shapes,
        Dictionary<int, int> nodeToBoneIndex,
        List<ImportedBone> bones,
        List<ImportedSubObject> subObjects,
        NifPropertyTargetMap targets,
        AssetImportContext context)
    {
        // Bones shared by shapes get one inverse bind; disagreements fail.
        var inverseBinds = new Dictionary<int, Matrix4x4>();

        foreach (var (shapeBlockIndex, shape) in shapes)
        {
            var instance = (FileFormatsNif.NifSkinInstance)file.Blocks[shape.SkinInstanceReference];
            var skinData = (FileFormatsNif.NifSkinData)file.Blocks[instance.DataReference];
            var root = (FileFormatsNif.NifNode)file.Blocks[instance.SkeletonRootReference];

            if (file.Roots.Count != 1 || instance.SkeletonRootReference != file.Roots[0])
            {
                throw new NotSupportedException(
                    $"NIF shape '{shape.Name}' is skinned against skeleton root '{root.Name}', but only the model root is supported.");
            }

            if (!skinData.HasVertexWeights)
            {
                throw new NotSupportedException(
                    $"NIF shape '{shape.Name}' has a NiSkinData block without vertex weights.");
            }

            if (shape.DataReference < 0 ||
                file.Blocks[shape.DataReference] is not FileFormatsNif.NifTriShapeData data)
            {
                throw new InvalidDataException(
                    $"NIF shape '{shape.Name}' has no geometry data.");
            }

            var skin = NifTransformConverter.CreateSkinTransformMatrix(skinData.SkinTransform);
            if (!Matrix4x4.Invert(skin, out var inverseSkin))
            {
                throw new InvalidDataException(
                    $"NIF shape '{shape.Name}' has a non-invertible skin transform.");
            }

            var influences = new List<(int BoneIndex, float Weight)>[data.NumVertices];
            for (var i = 0; i < influences.Length; i++)
            {
                influences[i] = [];
            }

            for (var b = 0; b < instance.BoneReferences.Count; b++)
            {
                var boneReference = instance.BoneReferences[b];
                var boneNode = (FileFormatsNif.NifNode)file.Blocks[boneReference];
                if (!nodeToBoneIndex.TryGetValue(boneReference, out var runtimeBoneIndex))
                {
                    throw new NotSupportedException(
                        $"NIF shape '{shape.Name}' references node '{boneNode.Name}' as a skin bone, but it is not a runtime bone (non-unit-scale subtree or dropped LOD); support for that is C4f work.");
                }

                AccumulateInfluences(shape, boneNode.Name, data, skinData.Bones[b].VertexWeights,
                    runtimeBoneIndex, influences);

                var sourceBind = skin * NifTransformConverter.CreateSkinTransformMatrix(
                    skinData.Bones[b].Transform);
                var engineBind = NifTransformConverter.ConjugateSourceToEngine(sourceBind);
                if (!IsFinite(engineBind) || !Matrix4x4.Invert(engineBind, out _))
                {
                    throw new InvalidDataException(
                        $"NIF shape '{shape.Name}' bone '{boneNode.Name}' has a degenerate inverse bind matrix.");
                }

                if (inverseBinds.TryGetValue(runtimeBoneIndex, out var existing))
                {
                    if (!MatricesEqualWithin(existing, engineBind, BindTolerance))
                    {
                        throw new InvalidDataException(
                            $"NIF shape '{shape.Name}' assigns a conflicting inverse bind to bone '{boneNode.Name}' shared with another shape.");
                    }
                }

                else
                {
                    inverseBinds[runtimeBoneIndex] = engineBind;
                }
            }

            var subObject = ConvertShape(
                file, shape, data, inverseSkin, influences, subObjects.Count, modelName, context);
            subObjects.Add(subObject);
            targets.RecordShape(
                shapeBlockIndex, shape.Name, subObject.FullName,
                NifSceneConverter.GatherShapePropertyTypes(file, shape));
        }

        for (var i = 0; i < bones.Count; i++)
        {
            if (inverseBinds.TryGetValue(i, out var bind))
            {
                var source = bones[i];
                bones[i] = new ImportedBone(
                    source.Name,
                    source.ParentIndex,
                    source.BindTranslation,
                    source.BindRotation,
                    bind);
            }
        }
    }

    private static void AccumulateInfluences(
        FileFormatsNif.NifTriShape shape,
        string boneName,
        FileFormatsNif.NifTriShapeData data,
        IReadOnlyList<FileFormatsNif.NifSkinVertexWeight> vertexWeights,
        int runtimeBoneIndex,
        List<(int BoneIndex, float Weight)>[] influences)
    {
        var seen = new HashSet<ushort>();
        foreach (var vertexWeight in vertexWeights)
        {
            var vertexIndex = vertexWeight.VertexIndex;
            var weight = vertexWeight.Weight;
            if (vertexIndex >= data.NumVertices)
            {
                throw new InvalidDataException(
                    $"NIF shape '{shape.Name}' bone '{boneName}' references vertex {vertexIndex}, but the shape has {data.NumVertices} vertices.");
            }

            if (!seen.Add(vertexIndex))
            {
                throw new InvalidDataException(
                    $"NIF shape '{shape.Name}' bone '{boneName}' weights vertex {vertexIndex} more than once.");
            }

            if (!float.IsFinite(weight) || weight < 0.0f || weight > 1.0f)
            {
                throw new InvalidDataException(
                    $"NIF shape '{shape.Name}' bone '{boneName}' has weight {weight} for vertex {vertexIndex}, outside [0, 1].");
            }

            influences[vertexIndex].Add((runtimeBoneIndex, weight));
        }
    }

    private static ImportedSubObject ConvertShape(
        FileFormatsNif.NifFile file,
        FileFormatsNif.NifTriShape shape,
        FileFormatsNif.NifTriShapeData data,
        Matrix4x4 inverseSkin,
        List<(int BoneIndex, float Weight)>[] influences,
        int subObjectIndex,
        string modelName,
        AssetImportContext context)
    {
        var bake = NifTransformConverter.CreateBakeMatrix(inverseSkin);
        var reverseWinding = NifTransformConverter.BakeDeterminant(bake) < 0;

        var vertexCount = data.NumVertices;
        var vertices = new ImportedVertex[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            vertices[i] = CreateVertex(shape, i, influences[i], data, bake);
        }

        var indices = NifSceneConverter.CopyTriangleIndices(data, reverseWinding);
        var material = NifSceneConverter.ConvertShapeMaterial(file, shape, context);
        var textureCoordinates = NifSceneConverter.ConvertTextureCoordinates(data);
        var meshPart = NifSceneConverter.CreateMeshPart(
            textureCoordinates, (uint)indices.Count, material);

        var positions = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            positions[i] = vertices[i].Position;
        }

        var (boundingBox, boundingSphere) = NifSceneConverter.ComputeBounds(positions);

        var mesh = new ImportedMesh(
            vertices,
            indices,
            boundingBox,
            boundingSphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: true,
            materials: [material],
            meshParts: [meshPart]);

        // Skinned meshes attach to the model root; the skeleton drives them.
        return NifSceneConverter.CreateSubObject(
            modelName, shape.Name, subObjectIndex, parentBoneIndex: 0, mesh);
    }

    private static ImportedVertex CreateVertex(
        FileFormatsNif.NifTriShape shape,
        int vertexIndex,
        List<(int BoneIndex, float Weight)> rawInfluences,
        FileFormatsNif.NifTriShapeData data,
        Matrix4x4 bake)
    {
        var influences = new List<(int BoneIndex, float Weight)>();
        var sum = 0.0f;
        foreach (var influence in rawInfluences)
        {
            if (influence.Weight > WeightTolerance)
            {
                influences.Add(influence);
                sum += influence.Weight;
            }
        }

        if (influences.Count > 4)
        {
            throw new NotSupportedException(
                $"NIF shape '{shape.Name}' vertex {vertexIndex} has {influences.Count} meaningful skin influences; at most 4 are supported.");
        }

        if (MathF.Abs(sum - 1.0f) > WeightSumTolerance)
        {
            throw new InvalidDataException(
                $"NIF shape '{shape.Name}' vertex {vertexIndex} has skin weights summing to {sum}, more than 0.01 from 1.");
        }

        influences.Sort(static (a, b) =>
            b.Weight != a.Weight ? b.Weight.CompareTo(a.Weight) : a.BoneIndex.CompareTo(b.BoneIndex));

        var position = NifTransformConverter.TransformPosition(data.Vertices[vertexIndex], bake);
        var normal = NifTransformConverter.TransformNormal(data.Normals![vertexIndex], bake);
        return new ImportedVertex(
            position,
            normal,
            secondaryPosition: position,
            secondaryNormal: normal,
            boneIndex0: SlotBone(influences, 0),
            boneIndex1: SlotBone(influences, 1),
            boneIndex2: SlotBone(influences, 2),
            boneIndex3: SlotBone(influences, 3),
            boneWeight0: SlotWeight(influences, 0, sum),
            boneWeight1: SlotWeight(influences, 1, sum),
            boneWeight2: SlotWeight(influences, 2, sum),
            boneWeight3: SlotWeight(influences, 3, sum));
    }

    private static uint SlotBone(List<(int BoneIndex, float Weight)> influences, int slot)
    {
        return slot < influences.Count ? (uint)influences[slot].BoneIndex : 0u;
    }

    private static float SlotWeight(List<(int BoneIndex, float Weight)> influences, int slot, float sum)
    {
        return slot < influences.Count ? influences[slot].Weight / sum : 0.0f;
    }

    private static bool IsFinite(Matrix4x4 value)
    {
        return float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
            && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
            && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
            && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
    }

    private static bool MatricesEqualWithin(Matrix4x4 a, Matrix4x4 b, float tolerance)
    {
        return MathF.Abs(a.M11 - b.M11) <= tolerance && MathF.Abs(a.M12 - b.M12) <= tolerance
            && MathF.Abs(a.M13 - b.M13) <= tolerance && MathF.Abs(a.M14 - b.M14) <= tolerance
            && MathF.Abs(a.M21 - b.M21) <= tolerance && MathF.Abs(a.M22 - b.M22) <= tolerance
            && MathF.Abs(a.M23 - b.M23) <= tolerance && MathF.Abs(a.M24 - b.M24) <= tolerance
            && MathF.Abs(a.M31 - b.M31) <= tolerance && MathF.Abs(a.M32 - b.M32) <= tolerance
            && MathF.Abs(a.M33 - b.M33) <= tolerance && MathF.Abs(a.M34 - b.M34) <= tolerance
            && MathF.Abs(a.M41 - b.M41) <= tolerance && MathF.Abs(a.M42 - b.M42) <= tolerance
            && MathF.Abs(a.M43 - b.M43) <= tolerance && MathF.Abs(a.M44 - b.M44) <= tolerance;
    }
}
