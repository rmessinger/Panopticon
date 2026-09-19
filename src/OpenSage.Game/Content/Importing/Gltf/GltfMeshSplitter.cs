#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Splits one primitive's converted vertices and triangles into runtime-sized meshes: each
/// split holds at most 65,535 distinct vertices, splits at triangle boundaries in source
/// order, rebases indices, and duplicates vertices referenced again after a split.
/// </summary>
internal static class GltfMeshSplitter
{
    public const int MaxVerticesPerMesh = ushort.MaxValue;

    public sealed class MeshSplit
    {
        public List<ImportedVertex> Vertices { get; } = [];

        public List<int> OriginalIndices { get; } = [];

        /// <summary>Source vertex index of each split vertex, in split order.</summary>
        public List<int> SourceVertexIndices { get; } = [];
    }

    /// <summary>
    /// Greedy source-order split: walk triangles, assigning each referenced vertex a local
    /// index in the current split, duplicating it into the next split when it reappears after
    /// a boundary.
    /// </summary>
    public static List<MeshSplit> Split(
        IReadOnlyList<ImportedVertex> vertices,
        IReadOnlyList<(int A, int B, int C)> triangles)
    {
        var splits = new List<MeshSplit>();
        var current = new MeshSplit();
        var localIndices = new Dictionary<int, int>();
        splits.Add(current);

        foreach (var (a, b, c) in triangles)
        {
            if (current.Vertices.Count + CountNewVertices(a, b, c, localIndices) > MaxVerticesPerMesh)
            {
                current = new MeshSplit();
                localIndices.Clear();
                splits.Add(current);
            }

            AppendIndex(a);
            AppendIndex(b);
            AppendIndex(c);

            void AppendIndex(int originalIndex)
            {
                if (!localIndices.TryGetValue(originalIndex, out var localIndex))
                {
                    localIndex = current.Vertices.Count;
                    localIndices.Add(originalIndex, localIndex);
                    current.Vertices.Add(vertices[originalIndex]);
                    current.SourceVertexIndices.Add(originalIndex);
                }

                current.OriginalIndices.Add(localIndex);
            }
        }

        return splits;
    }

    private static int CountNewVertices(int a, int b, int c, Dictionary<int, int> localIndices)
    {
        var count = 0;
        if (!localIndices.ContainsKey(a))
        {
            count++;
        }

        if (!localIndices.ContainsKey(b))
        {
            count++;
        }

        if (!localIndices.ContainsKey(c))
        {
            count++;
        }

        return count;
    }
}
