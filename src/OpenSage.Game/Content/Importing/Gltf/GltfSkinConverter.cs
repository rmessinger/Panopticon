#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SharpGLTF.Schema2;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Result of skin conversion: rebuilt skeleton with converted inverse binds and the joint
/// remap from skin-local indices to imported bone indices.
/// </summary>
internal sealed class GltfSkinConversion
{
    public ImportedSkeleton Skeleton { get; }

    public int[] JointToBoneIndex { get; }

    public GltfSkinConversion(ImportedSkeleton skeleton, int[] jointToBoneIndex)
    {
        Skeleton = skeleton;
        JointToBoneIndex = jointToBoneIndex;
    }
}

/// <summary>
/// Converts the single selected-scene skin: validates one skin per model, remaps joints into
/// imported bone order, converts inverse binds, and builds two-influence skin vertices whose
/// primary and secondary geometry share the same model-space data.
/// </summary>
internal static class GltfSkinConverter
{
    /// <summary>Influences at or below this weight are not meaningful.</summary>
    public const float WeightTolerance = 1e-4f;

    /// <summary>Allowed deviation of the weight sum from one before normalization.</summary>
    public const float WeightSumTolerance = 1e-2f;

    public static GltfSkinConversion ConvertSkin(
        ModelRoot root,
        GltfScenePlan plan,
        GltfImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        if (plan.SkinnedNodes.Count == 0)
        {
            throw new ArgumentException("The scene plan contains no skinned nodes.", nameof(plan));
        }

        var skinIndex = -1;
        foreach (var skinnedNode in plan.SkinnedNodes)
        {
            if (skinIndex == -1)
            {
                skinIndex = skinnedNode.SkinIndex;
            }
            else if (skinIndex != skinnedNode.SkinIndex)
            {
                throw new NotSupportedException(
                    $"glTF model uses skin {skinnedNode.SkinIndex} and skin {skinnedNode.SkinIndex}; " +
                    "multiple skins in one model are outside the supported subset.");
            }
        }

        var skin = root.LogicalSkins[skinIndex];
        var boneIndexBySourceNode = new Dictionary<int, int>();
        foreach (var nodePlan in plan.Nodes)
        {
            boneIndexBySourceNode[nodePlan.SourceNodeIndex] = nodePlan.BoneIndex;
        }

        var joints = skin.Joints;
        var inverseBinds = skin.InverseBindMatrices;
        var jointToBoneIndex = new int[joints.Count];
        for (var i = 0; i < joints.Count; i++)
        {
            var jointNode = joints[i];
            if (!boneIndexBySourceNode.TryGetValue(jointNode.LogicalIndex, out var boneIndex))
            {
                throw new InvalidDataException(
                    $"Skin joint '{jointNode.Name}' is not reachable in the selected scene.");
            }

            jointToBoneIndex[i] = boneIndex;
        }

        var basis = GltfTransformConverter.CreateBasis(options.UnitsPerMeter);

        var boneCount = plan.Skeleton.Bones.Count;
        var bonesByJoint = new Matrix4x4?[boneCount];
        for (var i = 0; i < joints.Count; i++)
        {
            // Per glTF, an absent inverse-bind accessor means identity for every joint.
            var converted = inverseBinds.Count == 0
                ? Matrix4x4.Identity
                : ConvertInverseBind(inverseBinds[i], basis, joints[i].Name);
            bonesByJoint[jointToBoneIndex[i]] = converted;
        }

        if (inverseBinds.Count != 0 && inverseBinds.Count < joints.Count)
        {
            throw new InvalidDataException(
                $"Skin has {joints.Count} joints but only {inverseBinds.Count} inverse-bind matrices.");
        }

        var bones = new ImportedBone[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            var source = plan.Skeleton.Bones[i];
            bones[i] = bonesByJoint[i] is { } inverseBind
                ? new ImportedBone(source.Name, source.ParentIndex, source.BindTranslation, source.BindRotation, inverseBind)
                : source;
        }

        return new GltfSkinConversion(new ImportedSkeleton(plan.Skeleton.Name, bones), jointToBoneIndex);
    }

    public static GltfConvertedPrimitive ConvertSkinnedPrimitive(
        MeshPrimitive primitive,
        int sourceNodeIndex,
        GltfSkinConversion skin,
        GltfImportOptions options,
        GltfPrimitiveConverter.MaterialFactory materialFactory)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(materialFactory);

        var hasJoints = primitive.VertexAccessors.TryGetValue("JOINTS_0", out var jointsAccessor);
        var hasWeights = primitive.VertexAccessors.TryGetValue("WEIGHTS_0", out var weightsAccessor);
        if (!hasJoints || !hasWeights)
        {
            throw new InvalidDataException(
                $"Skinned primitive of mesh '{primitive.LogicalParent.Name}' must supply JOINTS_0 and WEIGHTS_0 together.");
        }

        var positions = DecodeRequiredVector3(primitive, "POSITION");
        var normals = DecodeRequiredVector3(primitive, "NORMAL", normalize: true);

        var jointsView = jointsAccessor!.AsVector4Array();
        var weightsView = weightsAccessor!.AsVector4Array();
        if (jointsView.Count != positions.Length || weightsView.Count != positions.Length)
        {
            throw new InvalidDataException(
                $"Skinned primitive of mesh '{primitive.LogicalParent.Name}' has JOINTS_0/WEIGHTS_0 counts " +
                $"({jointsView.Count}/{weightsView.Count}) that do not match POSITION ({positions.Length}).");
        }

        var basis = GltfTransformConverter.CreateBasis(options.UnitsPerMeter);
        var vertices = new ImportedVertex[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            var position = GltfTransformConverter.ConvertPosition(positions[i], basis);
            var normal = Vector3.Normalize(GltfTransformConverter.ConvertDirection(normals[i]));

            var (boneIndex0, boneIndex1, weight0, weight1) = DecodeInfluences(
                jointsView[i], weightsView[i], skin.JointToBoneIndex, primitive, i);

            vertices[i] = new ImportedVertex(
                position,
                normal,
                secondaryPosition: position,
                secondaryNormal: normal,
                boneIndex0: boneIndex0,
                boneIndex1: boneIndex1,
                boneWeight0: weight0,
                boneWeight1: weight1);
        }

        var triangles = GltfAccessorDecoder.CopyTriangles(primitive, positions.Length);
        var splits = GltfMeshSplitter.Split(vertices, triangles);

        System.Numerics.Vector2[]? uv0 = null;
        if (primitive.VertexAccessors.TryGetValue("TEXCOORD_0", out var uv0Accessor))
        {
            uv0 = GltfAccessorDecoder.CopyTextureCoordinates(uv0Accessor, "TEXCOORD_0");
        }

        var meshes = new List<ImportedMesh>(splits.Count);
        foreach (var split in splits)
        {
            meshes.Add(GltfPrimitiveConverter.BuildMesh(
                split,
                uv0,
                null,
                materialFactory(primitive.Material),
                isSkinned: true));
        }

        return new GltfConvertedPrimitive(meshes, primitive.Material?.LogicalIndex ?? -1, sourceNodeIndex);
    }

    private static (uint BoneIndex0, uint BoneIndex1, float Weight0, float Weight1) DecodeInfluences(
        System.Numerics.Vector4 joints,
        System.Numerics.Vector4 weights,
        int[] jointToBoneIndex,
        MeshPrimitive primitive,
        int vertexIndex)
    {
        var slots = new (uint Joint, float Weight, int Slot)[4];
        var sum = 0f;
        for (var i = 0; i < 4; i++)
        {
            var weight = GetComponent(weights, i);
            var joint = GetComponent(joints, i);

            if (!float.IsFinite(weight) || weight < 0)
            {
                throw new InvalidDataException(
                    $"Skinned primitive of mesh '{primitive.LogicalParent.Name}' vertex {vertexIndex} has an invalid weight.");
            }

            if (weight <= WeightTolerance)
            {
                continue;
            }

            if (i >= 2)
            {
                throw new NotSupportedException(
                    $"Skinned primitive of mesh '{primitive.LogicalParent.Name}' vertex {vertexIndex} has a meaningful " +
                    $"third or fourth influence ({weight}); the runtime supports two.");
            }

            var jointIndex = (int)joint;
            if (jointIndex < 0 || jointIndex >= jointToBoneIndex.Length)
            {
                throw new InvalidDataException(
                    $"Skinned primitive of mesh '{primitive.LogicalParent.Name}' vertex {vertexIndex} references joint " +
                    $"{jointIndex}, but the skin has {jointToBoneIndex.Length} joints.");
            }

            slots[i] = ((uint)jointToBoneIndex[jointIndex], weight, i);
            sum += weight;
        }

        if (sum <= 0f || MathF.Abs(sum - 1f) > WeightSumTolerance)
        {
            throw new InvalidDataException(
                $"Skinned primitive of mesh '{primitive.LogicalParent.Name}' vertex {vertexIndex} has a weight sum of " +
                $"{sum}; sums must be positive and within {WeightSumTolerance} of one.");
        }

        var first = slots[0];
        var second = slots[1];
        if (second.Weight > first.Weight)
        {
            (first, second) = (second, first);
        }

        return (first.Joint, second.Joint, first.Weight / sum, second.Weight / sum);
    }

    private static float GetComponent(System.Numerics.Vector4 vector, int index)
    {
        return index switch
        {
            0 => vector.X,
            1 => vector.Y,
            2 => vector.Z,
            _ => vector.W,
        };
    }

    private static System.Numerics.Vector3[] DecodeRequiredVector3(
        MeshPrimitive primitive,
        string attribute,
        bool normalize = false)
    {
        if (!primitive.VertexAccessors.TryGetValue(attribute, out var accessor))
        {
            throw new InvalidDataException(
                $"Skinned primitive of mesh '{primitive.LogicalParent.Name}' is missing required attribute '{attribute}'.");
        }

        var result = GltfAccessorDecoder.CopyPositions(accessor, attribute);
        if (normalize)
        {
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = System.Numerics.Vector3.Normalize(result[i]);
            }
        }

        return result;
    }

    private static Matrix4x4 ConvertInverseBind(Matrix4x4 inverseBind, Matrix4x4 basis, string jointName)
    {
        var converted = GltfTransformConverter.ConvertMatrix(inverseBind, basis);

        if (!IsFinite(converted) || !Matrix4x4.Invert(converted, out _))
        {
            throw new InvalidDataException(
                $"Skin joint '{jointName}' has a non-finite or non-invertible inverse-bind matrix after conversion.");
        }

        return converted;
    }

    private static bool IsFinite(Matrix4x4 matrix)
    {
        return float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14) &&
               float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24) &&
               float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34) &&
               float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
    }
}
