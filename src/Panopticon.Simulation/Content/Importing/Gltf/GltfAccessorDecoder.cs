#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SharpGLTF.Schema2;

namespace Panopticon.Content.Importing.Gltf;

/// <summary>
/// Copies accessor-backed attribute and index data into engine-owned arrays immediately, so no
/// parser memory escapes the decoder. SharpGLTF's accessor views handle interleaved buffer
/// views, normalized integer components, sparse overlays, and accessor byte offsets.
/// </summary>
internal static class GltfAccessorDecoder
{
    public static float[] CopyScalars(Accessor accessor, string attributeName)
    {
        var view = accessor.AsScalarArray();
        var result = new float[view.Count];
        for (var i = 0; i < view.Count; i++)
        {
            result[i] = view[i];
        }

        ValidateFinite(result, attributeName);
        return result;
    }

    public static Vector2[] CopyTextureCoordinates(Accessor accessor, string attributeName)
    {
        var view = accessor.AsVector2Array();
        var result = new System.Numerics.Vector2[view.Count];
        for (var i = 0; i < view.Count; i++)
        {
            result[i] = new System.Numerics.Vector2(view[i].X, view[i].Y);
        }

        ValidateFinite(result, attributeName);
        return result;
    }

    public static System.Numerics.Vector3[] CopyPositions(Accessor accessor, string attributeName)
    {
        var view = accessor.AsVector3Array();
        var result = new System.Numerics.Vector3[view.Count];
        for (var i = 0; i < view.Count; i++)
        {
            result[i] = new System.Numerics.Vector3(view[i].X, view[i].Y, view[i].Z);
        }

        ValidateFinite(result, attributeName);
        return result;
    }

    public static System.Numerics.Vector3[] CopyNormals(Accessor accessor, string attributeName)
    {
        var result = CopyPositions(accessor, attributeName);
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = System.Numerics.Vector3.Normalize(result[i]);
        }

        return result;
    }

    /// <summary>
    /// Tangents keep their handedness component: xyz in the vector, w (glTF stores -1 or 1)
    /// returned separately.
    /// </summary>
    public static (System.Numerics.Vector3[] Tangents, float[] Handedness) CopyTangents(
        Accessor accessor,
        string attributeName)
    {
        var view = accessor.AsVector4Array();
        var tangents = new System.Numerics.Vector3[view.Count];
        var handedness = new float[view.Count];
        for (var i = 0; i < view.Count; i++)
        {
            tangents[i] = new System.Numerics.Vector3(view[i].X, view[i].Y, view[i].Z);
            handedness[i] = view[i].W;
        }

        ValidateFinite(tangents, attributeName);
        return (tangents, handedness);
    }

    /// <summary>
    /// Triangle vertex-index triples for a triangle primitive. Indices are decoded from the
    /// index accessor in authored order (GetTriangleIndices canonicalizes winding, which would
    /// lose orientation); non-indexed primitives get sequential triples.
    /// </summary>
    public static List<(int A, int B, int C)> CopyTriangles(MeshPrimitive primitive, int vertexCount)
    {
        List<int> indices;
        if (primitive.IndexAccessor is { } indexAccessor)
        {
            if (indexAccessor.Count % 3 != 0)
            {
                throw new InvalidDataException(
                    $"Primitive of mesh '{primitive.LogicalParent.Name}' has {indexAccessor.Count} indices, " +
                    "which is not a multiple of three.");
            }

            var indexView = indexAccessor.AsIndicesArray();
            indices = new List<int>(indexView.Count);
            for (var i = 0; i < indexView.Count; i++)
            {
                indices.Add(checked((int)indexView[i]));
            }
        }
        else
        {
            indices = new List<int>(vertexCount);
            for (var i = 0; i < vertexCount; i++)
            {
                indices.Add(i);
            }
        }

        var triangles = new List<(int, int, int)>(indices.Count / 3);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = indices[i];
            var b = indices[i + 1];
            var c = indices[i + 2];
            if (a < 0 || a >= vertexCount || b < 0 || b >= vertexCount || c < 0 || c >= vertexCount)
            {
                throw new InvalidDataException(
                    $"Primitive of mesh '{primitive.LogicalParent.Name}' has an index outside its vertex range [0, {vertexCount}).");
            }

            triangles.Add((a, b, c));
        }

        if (triangles.Count == 0)
        {
            throw new InvalidDataException(
                $"Primitive of mesh '{primitive.LogicalParent.Name}' contains no triangles.");
        }

        return triangles;
    }

    private static void ValidateFinite(float[] values, string attributeName)
    {
        foreach (var value in values)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException($"Attribute '{attributeName}' contains a non-finite value.");
            }
        }
    }

    private static void ValidateFinite(System.Numerics.Vector2[] values, string attributeName)
    {
        foreach (var value in values)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new InvalidDataException($"Attribute '{attributeName}' contains a non-finite value.");
            }
        }
    }

    private static void ValidateFinite(System.Numerics.Vector3[] values, string attributeName)
    {
        foreach (var value in values)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            {
                throw new InvalidDataException($"Attribute '{attributeName}' contains a non-finite value.");
            }
        }
    }
}
