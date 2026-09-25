#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Content.Importing.Gltf;

/// <summary>
/// One converted scene node: its bone in the imported skeleton plus source identity for
/// later primitive and animation conversion.
/// </summary>
internal sealed class GltfNodePlan
{
    public int BoneIndex { get; }

    public int SourceNodeIndex { get; }

    /// <summary>Authored node name; null when the source node was unnamed.</summary>
    public string? AuthoredName { get; }

    public string EffectiveName { get; }

    public GltfNodePlan(int boneIndex, int sourceNodeIndex, string? authoredName, string effectiveName)
    {
        BoneIndex = boneIndex;
        SourceNodeIndex = sourceNodeIndex;
        AuthoredName = authoredName;
        EffectiveName = effectiveName;
    }
}

/// <summary>
/// Rigid attachment of one unskinned mesh node's primitives: geometry scale remaining after
/// uniform scale was absorbed into the skeleton, its inverse-transpose normal matrix, and the
/// tangent re-orthogonalization requirement for non-uniform scaling.
/// </summary>
internal sealed class GltfAttachmentPlan
{
    public int BoneIndex { get; }

    public int SourceNodeIndex { get; }

    public Matrix4x4 GeometryTransform { get; }

    public Matrix4x4 NormalMatrix { get; }

    public bool RequiresTangentReorthogonalization { get; }

    public GltfAttachmentPlan(
        int boneIndex,
        int sourceNodeIndex,
        in Matrix4x4 geometryTransform,
        in Matrix4x4 normalMatrix,
        bool requiresTangentReorthogonalization)
    {
        BoneIndex = boneIndex;
        SourceNodeIndex = sourceNodeIndex;
        GeometryTransform = geometryTransform;
        NormalMatrix = normalMatrix;
        RequiresTangentReorthogonalization = requiresTangentReorthogonalization;
    }
}

/// <summary>
/// A mesh node driven by a skin; primitive decoding stays with card 6, joint remapping and
/// inverse binds with card 7.
/// </summary>
internal sealed record GltfSkinnedNodePlan(int SourceNodeIndex, int SkinIndex);

/// <summary>
/// Deterministic scene conversion result: parent-before-child skeleton, per-node plan, rigid
/// attachments for unskinned mesh nodes, and skinned-node markers.
/// </summary>
internal sealed class GltfScenePlan
{
    public ImportedSkeleton Skeleton { get; }

    public IReadOnlyList<GltfNodePlan> Nodes { get; }

    public IReadOnlyList<GltfAttachmentPlan> Attachments { get; }

    public IReadOnlyList<GltfSkinnedNodePlan> SkinnedNodes { get; }

    public GltfScenePlan(
        ImportedSkeleton skeleton,
        IReadOnlyList<GltfNodePlan> nodes,
        IReadOnlyList<GltfAttachmentPlan> attachments,
        IReadOnlyList<GltfSkinnedNodePlan> skinnedNodes)
    {
        Skeleton = skeleton;
        Nodes = nodes;
        Attachments = attachments;
        SkinnedNodes = skinnedNodes;
    }
}
