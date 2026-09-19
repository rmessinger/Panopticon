#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SharpGLTF.Schema2;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Converts the selected glTF scene into a deterministic parent-before-child skeleton plan
/// with rigid attachments, applying the documented basis, units, and scale policy.
/// </summary>
internal static class GltfSceneConverter
{
    private const string SyntheticRootName = "__gltf_synth_root__";
    private const string UnnamedNodeNamePrefix = "__gltf_node_";

    public static GltfScenePlan CreatePlan(
        string modelPath,
        ModelRoot root,
        GltfImportOptions options,
        bool hasDeclaredDefaultScene)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);

        var scene = SelectScene(root, hasDeclaredDefaultScene);

        var basis = GltfTransformConverter.CreateBasis(options.UnitsPerMeter);

        var bones = new List<ImportedBone>();
        var nodes = new List<GltfNodePlan>();
        var attachments = new List<GltfAttachmentPlan>();
        var skinnedNodes = new List<GltfSkinnedNodePlan>();
        var authoredNames = CollectAuthoredNames(root);

        var sceneRoots = new List<Node>(scene.VisualChildren);
        var syntheticRootInserted = sceneRoots.Count > 1;

        var skinIndices = new Dictionary<Skin, int>();
        var jointNodes = new HashSet<Node>();
        CollectSkins(root, skinIndices, jointNodes);

        var boneIndexByNode = new Dictionary<Node, int>();

        if (syntheticRootInserted)
        {
            bones.Add(new ImportedBone(SyntheticRootName, -1, Vector3.Zero, Quaternion.Identity));
        }

        foreach (var sceneRoot in sceneRoots)
        {
            VisitNode(
                sceneRoot,
                parentBoneIndex: syntheticRootInserted ? 0 : -1,
                inheritedUniformScale: 1f,
                inheritedNonUniform: false,
                inheritedScaleOnJointPath: false);
        }

        return new GltfScenePlan(
            new ImportedSkeleton(GetSkeletonName(modelPath), bones),
            nodes,
            attachments,
            skinnedNodes);

        void VisitNode(
            Node node,
            int parentBoneIndex,
            float inheritedUniformScale,
            bool inheritedNonUniform,
            bool inheritedScaleOnJointPath)
        {
            if (!Matrix4x4.Decompose(node.LocalMatrix, out var scale, out var rotation, out var translation))
            {
                throw new NotSupportedException(
                    $"glTF node '{node.Name}' has a transform that does not decompose into scale, rotation, and translation (shear).");
            }

            if (node.LocalMatrix.GetDeterminant() < 0)
            {
                throw new NotSupportedException(
                    $"glTF node '{node.Name}' has a mirrored (negative-determinant) transform.");
            }

            var isUniformScale = MathF.Abs(scale.X - scale.Y) < 1e-5f &&
                                 MathF.Abs(scale.Y - scale.Z) < 1e-5f;
            var nodeScaleIsIdentity = MathF.Abs(scale.X - 1f) < 1e-5f &&
                                      MathF.Abs(scale.Y - 1f) < 1e-5f &&
                                      MathF.Abs(scale.Z - 1f) < 1e-5f;

            var isLeafNode = true;
            foreach (var child in node.VisualChildren)
            {
                isLeafNode = false;
                break;
            }

            var isJointPath = inheritedScaleOnJointPath || jointNodes.Contains(node);

            if (!nodeScaleIsIdentity && isJointPath)
            {
                throw new NotSupportedException(
                    $"glTF node '{node.Name}' carries scale on a skin joint or one of its ancestors; skin paths require identity scale.");
            }

            if (!isUniformScale)
            {
                // Positive non-uniform scale is only valid on a leaf unskinned mesh node.
                var isLeafUnskinnedMeshNode = node.Mesh != null && node.Skin == null && isLeafNode;
                if (!isLeafUnskinnedMeshNode)
                {
                    throw new NotSupportedException(
                        $"glTF node '{node.Name}' has non-uniform scale but is not a leaf unskinned mesh node.");
                }

                if (inheritedNonUniform)
                {
                    throw new NotSupportedException(
                        $"glTF node '{node.Name}' stacks non-uniform scale onto non-uniform ancestor scale.");
                }
            }

            var bindTranslation = GltfTransformConverter.ConvertPosition(translation, basis) * inheritedUniformScale;
            var bindRotation = GltfTransformConverter.ConvertRotation(rotation, basis);

            var boneIndex = bones.Count;
            var authoredName = string.IsNullOrEmpty(node.Name) ? null : node.Name;
            var effectiveName = authoredName ?? CreateUnnamedNodeName(node, authoredNames);
            bones.Add(new ImportedBone(effectiveName, parentBoneIndex, bindTranslation, bindRotation));
            boneIndexByNode[node] = boneIndex;
            nodes.Add(new GltfNodePlan(boneIndex, node.LogicalIndex, authoredName, effectiveName));

            var scaleCarriesNonUniform = inheritedNonUniform || !isUniformScale;
            var accumulatedUniformScale = inheritedUniformScale * (isUniformScale ? scale.X : 1f);

            if (node.Mesh != null)
            {
                if (node.Skin != null)
                {
                    skinnedNodes.Add(new GltfSkinnedNodePlan(
                        node.LogicalIndex,
                        skinIndices[node.Skin]));
                }
                else
                {
                    Matrix4x4 geometryTransform = isUniformScale
                        ? Matrix4x4.CreateScale(accumulatedUniformScale)
                        : Matrix4x4.CreateScale(inheritedUniformScale * scale.X, inheritedUniformScale * scale.Y, inheritedUniformScale * scale.Z);

                    Matrix4x4.Invert(geometryTransform, out var inverseGeometryTransform);
                    var normalMatrix = Matrix4x4.Transpose(inverseGeometryTransform);

                    attachments.Add(new GltfAttachmentPlan(
                        boneIndex,
                        node.LogicalIndex,
                        geometryTransform,
                        normalMatrix,
                        scaleCarriesNonUniform));
                }
            }

            foreach (var child in node.VisualChildren)
            {
                VisitNode(
                    child,
                    boneIndex,
                    accumulatedUniformScale,
                    scaleCarriesNonUniform,
                    isJointPath);
            }
        }
    }

    private static Scene SelectScene(ModelRoot root, bool hasDeclaredDefaultScene)
    {
        if (root.LogicalScenes.Count == 0)
        {
            throw new InvalidDataException("glTF document declares no scene.");
        }

        if (root.LogicalScenes.Count > 1 && !hasDeclaredDefaultScene)
        {
            throw new InvalidDataException(
                $"glTF document declares {root.LogicalScenes.Count} scenes and no default scene; one scene must be selected with scene:.");
        }

        return root.DefaultScene!;
    }

    private static string GetSkeletonName(string modelPath)
    {
        return Path.GetFileNameWithoutExtension(modelPath);
    }

    private static HashSet<string> CollectAuthoredNames(ModelRoot root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.LogicalNodes)
        {
            if (!string.IsNullOrEmpty(node.Name))
            {
                names.Add(node.Name);
            }
        }

        return names;
    }

    private static void CollectSkins(ModelRoot root, Dictionary<Skin, int> skinIndices, HashSet<Node> jointNodes)
    {
        for (var i = 0; i < root.LogicalSkins.Count; i++)
        {
            var skin = root.LogicalSkins[i];
            skinIndices[skin] = i;
            foreach (var joint in skin.Joints)
            {
                jointNodes.Add(joint);
            }
        }
    }

    private static string CreateUnnamedNodeName(Node node, HashSet<string> authoredNames)
    {
        string candidate;
        var attempt = 0;
        do
        {
            candidate = $"{UnnamedNodeNamePrefix}{node.LogicalIndex}" + (attempt == 0 ? "" : $"_{attempt}");
            attempt++;
        }
        while (authoredNames.Contains(candidate));

        return candidate;
    }
}
