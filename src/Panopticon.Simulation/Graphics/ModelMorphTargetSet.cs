using System;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Graphics.Shaders;

namespace Panopticon.Graphics;

/// <summary>
/// Runtime materialization of one morph target set: named targets with per-vertex position
/// deltas applied on the CPU into a per-instance vertex buffer before existing skinning.
/// EE2 morphs carry position deltas only; base normals are preserved.
/// </summary>
internal sealed class ModelMorphTargetSet
{
    public readonly string PrimitiveId;
    public readonly string[] TargetNames;
    public readonly float[] DefaultWeights;

    /// <summary>Per-target position deltas, each of vertex-count length, in target order.</summary>
    public readonly Vector3[][] PositionDeltas;

    public readonly int VertexCount;

    private ModelMorphTargetSet(
        string primitiveId,
        string[] targetNames,
        float[] defaultWeights,
        Vector3[][] positionDeltas,
        int vertexCount)
    {
        PrimitiveId = primitiveId;
        TargetNames = targetNames;
        DefaultWeights = defaultWeights;
        PositionDeltas = positionDeltas;
        VertexCount = vertexCount;
    }

    public static ModelMorphTargetSet Create(ImportedMorphTargetSet importedSet, int vertexCount)
    {
        var targetCount = importedSet.TargetNames.Count;
        var targetNames = new string[targetCount];
        var defaultWeights = new float[targetCount];
        var positionDeltas = new Vector3[targetCount][];

        for (var t = 0; t < targetCount; t++)
        {
            var target = importedSet.Targets[t];
            if (target.NormalDeltas != null || target.TangentDeltas != null)
            {
                throw new NotSupportedException(
                    $"Morph target '{importedSet.TargetNames[t]}' carries normal or tangent deltas; " +
                    "the C4g CPU morph runtime supports position deltas only.");
            }

            var sourceDeltas = target.PositionDeltas
                ?? throw new ArgumentException(
                    $"Morph target '{importedSet.TargetNames[t]}' has no position deltas.",
                    nameof(importedSet));
            if (sourceDeltas.Count != vertexCount)
            {
                throw new ArgumentException(
                    $"Morph target '{importedSet.TargetNames[t]}' covers {sourceDeltas.Count} vertices " +
                    $"but the mesh has {vertexCount}.",
                    nameof(importedSet));
            }

            var deltas = new Vector3[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                deltas[i] = sourceDeltas[i];
            }

            targetNames[t] = importedSet.TargetNames[t];
            defaultWeights[t] = importedSet.DefaultWeights[t];
            positionDeltas[t] = deltas;
        }

        return new ModelMorphTargetSet(
            importedSet.PrimitiveId,
            targetNames,
            defaultWeights,
            positionDeltas,
            vertexCount);
    }

    /// <summary>
    /// Applies weighted position deltas to the base vertices in target order. Skinning runs on
    /// the result unchanged (C6 fixed order: morph before skinning).
    /// </summary>
    public void Apply(
        ReadOnlySpan<MeshShaderResources.MeshVertex.Basic> baseVertices,
        ReadOnlySpan<float> weights,
        Span<MeshShaderResources.MeshVertex.Basic> result)
    {
        if (baseVertices.Length != VertexCount || result.Length != VertexCount)
            throw new ArgumentException(
                $"Morph target set '{PrimitiveId}' covers {VertexCount} vertices but was given {baseVertices.Length}.");

        if (weights.Length != TargetNames.Length)
            throw new ArgumentException(
                $"Morph target set '{PrimitiveId}' expects {TargetNames.Length} weights but received {weights.Length}.");

        for (var i = 0; i < VertexCount; i++)
        {
            result[i] = baseVertices[i];
        }

        for (var t = 0; t < TargetNames.Length; t++)
        {
            var weight = weights[t];
            if (weight == 0f)
            {
                continue;
            }

            var deltas = PositionDeltas[t];
            for (var i = 0; i < VertexCount; i++)
            {
                result[i].Position0 += weight * deltas[i];
            }
        }
    }
}
