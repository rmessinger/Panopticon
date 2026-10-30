#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.FileFormats.Granny;
using Panopticon.Mathematics;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Converts a Granny Mesh into <see cref="ImportedMesh"/> sub-objects in engine space.
/// Meshes with more than one bone binding import skinned (WI-C10c-01): vertices stay in
/// skeleton-root bind space with up to four normalized influences, attached to bone 0 like
/// the NIF skin path. Single-binding meshes keep the rigid C10b path, baked into their bone.
/// Vertex components are located by struct member name (Position, Normal, Tangent, Binormal,
/// DiffuseColor0, TextureCoordinates0); the VertexComponentNames list uses DCC set names that
/// can differ (e.g. map1, UnnamedColor0).
/// </summary>
internal static class GrannyMeshConverter
{
    private const int MaxVertices = ushort.MaxValue;

    public static IReadOnlyList<ImportedSubObject> Convert(
        GrannyObject mesh,
        ImportedSkeleton skeleton,
        IReadOnlyDictionary<string, int> boneIndexByName,
        GrannyAxisSystem axes,
        string sourceKey,
        int subObjectIndex,
        Aoe3MaterialConverter materialConverter)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var meshName = mesh.GetString("Name") ?? $"Mesh{subObjectIndex}";
        var vertexData = mesh.GetObject("PrimaryVertexData")
            ?? throw new InvalidDataException($"Granny file '{sourceKey}': mesh '{meshName}' has no vertex data.");
        var topology = mesh.GetObject("PrimaryTopology")
            ?? throw new InvalidDataException($"Granny file '{sourceKey}': mesh '{meshName}' has no topology.");

        var vertexObjects = vertexData.GetObjects("Vertices");
        if (vertexObjects.Count == 0)
        {
            throw new InvalidDataException($"Granny file '{sourceKey}': mesh '{meshName}' has no vertices.");
        }

        var groups = ReadGroups(topology, sourceKey, meshName);
        var indices = ReadIndices(topology, sourceKey, meshName);
        var materialNames = ReadMaterialNames(mesh, sourceKey, meshName);
        var materialFileKey = BuildMaterialFileKey(sourceKey, meshName);

        var boneBindings = mesh.GetObjects("BoneBindings");
        var isSkinned = boneBindings.Count > 1;
        int[]? bindingToSkeleton = null;
        int parentBoneIndex;
        if (isSkinned)
        {
            // Skinned vertices live in skeleton-root bind space and attach to bone 0, like the
            // NIF skin path; the rigid bake below only applies to single-binding meshes.
            parentBoneIndex = 0;
            bindingToSkeleton = MapBindingsToSkeletonIndices(
                boneBindings, boneIndexByName, sourceKey, meshName);
        }
        else
        {
            parentBoneIndex = ResolveFirstBoundBone(boneBindings, skeleton, boneIndexByName, sourceKey, meshName);
        }

        var positions = new Vector3[vertexObjects.Count];
        var normals = new Vector3[vertexObjects.Count];
        var tangents = new Vector3?[vertexObjects.Count];
        var textureCoordinates = new Vector2?[vertexObjects.Count];
        var hasTextureCoordinates = false;
        var skinBoneIndices = isSkinned ? new byte[vertexObjects.Count * 4] : null;
        var skinWeights = isSkinned ? new float[vertexObjects.Count * 4] : null;

        for (var i = 0; i < vertexObjects.Count; i++)
        {
            var vertex = vertexObjects[i];

            var filePosition = ReadPosition(vertex, sourceKey, meshName, i);
            var fileNormal = ReadUnitVector(vertex.Get("Normal"), sourceKey, meshName, i, "Normal")
                ?? -Vector3.UnitY;
            var fileTangent = ReadUnitVector(vertex.Get("Tangent"), sourceKey, meshName, i, "Tangent");

            positions[i] = axes.ToEnginePosition(filePosition);
            normals[i] = Vector3.Normalize(axes.ToEngineDirection(fileNormal));

            if (fileTangent is { } tangentValue)
            {
                tangents[i] = Vector3.Normalize(axes.ToEngineDirection(tangentValue));
            }

            if (ReadTextureCoordinates(vertex) is { } uv)
            {
                textureCoordinates[i] = uv;
                hasTextureCoordinates = true;
            }

            if (skinBoneIndices is not null && skinWeights is not null && bindingToSkeleton is not null)
            {
                var influences = AggregateInfluences(
                    bindingToSkeleton,
                    ReadInfluenceArray(vertex.Get("BoneIndices"), 4, sourceKey, meshName, i, "BoneIndices"),
                    ReadInfluenceArray(vertex.Get("BoneWeights"), 4, sourceKey, meshName, i, "BoneWeights"),
                    bindingToSkeleton[0]);
                skinBoneIndices[i * 4 + 0] = (byte)influences.BoneIndex0;
                skinBoneIndices[i * 4 + 1] = (byte)influences.BoneIndex1;
                skinBoneIndices[i * 4 + 2] = (byte)influences.BoneIndex2;
                skinBoneIndices[i * 4 + 3] = (byte)influences.BoneIndex3;
                skinWeights[i * 4 + 0] = influences.Weight0;
                skinWeights[i * 4 + 1] = influences.Weight1;
                skinWeights[i * 4 + 2] = influences.Weight2;
                skinWeights[i * 4 + 3] = influences.Weight3;
            }
        }

        // Rigid binding: geometry rides the first bound bone, so vertices are baked into that
        // bone's local space through its inverse bind matrix. Skinned vertices keep their
        // skeleton-root bind-space coordinates.
        if (!isSkinned)
        {
            var boneInverseBind = skeleton.Bones[parentBoneIndex].InverseBindMatrix;
            for (var i = 0; i < vertexObjects.Count; i++)
            {
                positions[i] = Vector3.Transform(positions[i], boneInverseBind);
                normals[i] = Vector3.Normalize(Vector3.TransformNormal(normals[i], boneInverseBind));

                if (tangents[i] is { } tangent)
                {
                    tangents[i] = Vector3.Normalize(Vector3.TransformNormal(tangent, boneInverseBind));
                }
            }
        }

        // A mirrored basis (negative-determinant right/up/back) swaps apparent winding.
        var reverseWinding = axes.Mirrors;

        var subObjects = new List<ImportedSubObject>();

        if (vertexObjects.Count <= MaxVertices)
        {
            var meshIndices = BuildIndexBuffer(indices, groups, 0, groups.Count, reverseWinding);
            subObjects.Add(CreateSubObject(
                meshName, sourceKey, subObjectIndex, parentBoneIndex, mesh,
                positions, normals, tangents,
                hasTextureCoordinates ? textureCoordinates : null,
                meshIndices, groups, 0, groups.Count, materialNames, materialConverter, materialFileKey,
                isSkinned, skinBoneIndices, skinWeights));
            return subObjects;
        }

        // Over the 16-bit vertex limit: split per topology group, remapping each group's
        // vertices into a compact buffer.
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var referencedByGroup = new Dictionary<int, int>();
            for (var t = 0; t < group.TriangleCount; t++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var source = indices[(group.FirstTriangle + t) * 3 + c];
                    if (!referencedByGroup.TryGetValue(source, out var _) && referencedByGroup.Count >= MaxVertices)
                    {
                        throw new NotSupportedException(
                            $"Granny file '{sourceKey}': mesh '{meshName}' group {g} references more than " +
                            $"{MaxVertices} vertices, exceeding the runtime index format.");
                    }

                    referencedByGroup.TryAdd(source, referencedByGroup.Count);
                }
            }

            var remap = referencedByGroup;
            var groupVertexCount = remap.Count;
            var groupPositions = new Vector3[groupVertexCount];
            var groupNormals = new Vector3[groupVertexCount];
            var groupTangents = new Vector3?[groupVertexCount];
            Vector2?[]? groupCoordinates = hasTextureCoordinates ? new Vector2?[groupVertexCount] : null;
            var groupSkinBoneIndices = isSkinned ? new byte[groupVertexCount * 4] : null;
            var groupSkinWeights = isSkinned ? new float[groupVertexCount * 4] : null;

            foreach (var (sourceIndex, targetIndex) in remap)
            {
                groupPositions[targetIndex] = positions[sourceIndex];
                groupNormals[targetIndex] = normals[sourceIndex];
                groupTangents[targetIndex] = tangents[sourceIndex];

                if (groupCoordinates is not null)
                {
                    groupCoordinates[targetIndex] = textureCoordinates[sourceIndex];
                }

                if (groupSkinBoneIndices is not null && groupSkinWeights is not null
                    && skinBoneIndices is not null && skinWeights is not null)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        groupSkinBoneIndices[targetIndex * 4 + c] = skinBoneIndices[sourceIndex * 4 + c];
                        groupSkinWeights[targetIndex * 4 + c] = skinWeights[sourceIndex * 4 + c];
                    }
                }
            }

            var groupIndices = new ushort[group.TriangleCount * 3];
            for (var t = 0; t < group.TriangleCount; t++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var source = indices[(group.FirstTriangle + t) * 3 + c];
                    groupIndices[t * 3 + (reverseWinding ? 2 - c : c)] = (ushort)remap[source];
                }
            }

            subObjects.Add(CreateSubObject(
                $"{meshName}_g{g}", sourceKey, subObjectIndex, parentBoneIndex, mesh,
                groupPositions, groupNormals, groupTangents,
                groupCoordinates,
                groupIndices,
                [group], 0, 1, materialNames, materialConverter, materialFileKey,
                isSkinned, groupSkinBoneIndices, groupSkinWeights));
        }

        return subObjects;
    }

    private sealed record Group(int MaterialIndex, int FirstTriangle, int TriangleCount);

    private static List<Group> ReadGroups(GrannyObject topology, string sourceKey, string meshName)
    {
        var groupObjects = topology.GetObjects("Groups");
        if (groupObjects.Count == 0)
        {
            throw new InvalidDataException($"Granny file '{sourceKey}': mesh '{meshName}' topology has no groups.");
        }

        var groups = new List<Group>(groupObjects.Count);
        foreach (var groupObject in groupObjects)
        {
            groups.Add(new Group(
                groupObject.GetInt32("MaterialIndex") ?? 0,
                groupObject.GetInt32("TriFirst") ?? 0,
                groupObject.GetInt32("TriCount") ?? 0));
        }

        return groups;
    }

    private static int[] ReadIndices(GrannyObject topology, string sourceKey, string meshName)
    {
        var wide = UnwrapValues(topology.GetObjects("Indices"));
        if (wide.Length > 0)
        {
            return wide;
        }

        var narrow = UnwrapValues(topology.GetObjects("Indices16"));
        if (narrow.Length == 0)
        {
            throw new InvalidDataException(
                $"Granny file '{sourceKey}': mesh '{meshName}' topology has neither Indices nor Indices16.");
        }

        return narrow;
    }

    /// <summary>
    /// Topology index arrays are arrays of one-member wrapper structs around the integer value.
    /// </summary>
    private static int[] UnwrapValues(IReadOnlyList<GrannyObject> items)
    {
        if (items.Count == 0)
        {
            return Array.Empty<int>();
        }

        var values = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            foreach (var field in items[i].Fields.Values)
            {
                if (field is int value)
                {
                    values[i] = value;
                }
            }
        }

        return values;
    }

    private static List<string?> ReadMaterialNames(GrannyObject mesh, string sourceKey, string meshName)
    {
        var bindings = mesh.GetObjects("MaterialBindings");
        var names = new List<string?>(bindings.Count);
        foreach (var binding in bindings)
        {
            names.Add(binding.GetObject("Material")?.GetString("Name"));
        }

        if (names.Count == 0)
        {
            throw new InvalidDataException($"Granny file '{sourceKey}': mesh '{meshName}' has no material bindings.");
        }

        return names;
    }

    private static int ResolveFirstBoundBone(
        IReadOnlyList<GrannyObject> boneBindings,
        ImportedSkeleton skeleton,
        IReadOnlyDictionary<string, int> boneIndexByName,
        string sourceKey,
        string meshName)
    {
        // Rigid C10b binding: the whole mesh follows the first bound bone even when the file
        // carries many bone bindings; per-vertex skinning is WI-C10c-01.
        var boneName = boneBindings.Count > 0 ? boneBindings[0].GetString("BoneName") : null;
        if (boneName is null || !boneIndexByName.TryGetValue(boneName, out var boneIndex))
        {
            if (skeleton.Bones.Count == 0)
            {
                throw new InvalidDataException(
                    $"Granny file '{sourceKey}': mesh '{meshName}' cannot be bound: the skeleton is empty.");
            }

            return 0;
        }

        return boneIndex;
    }

    /// <summary>
    /// Maps mesh bone-binding positions to skeleton bone indices. Vertex BoneIndices reference
    /// the binding list, not the skeleton, so every binding must resolve.
    /// </summary>
    private static int[] MapBindingsToSkeletonIndices(
        IReadOnlyList<GrannyObject> boneBindings,
        IReadOnlyDictionary<string, int> boneIndexByName,
        string sourceKey,
        string meshName)
    {
        var map = new int[boneBindings.Count];
        for (var i = 0; i < boneBindings.Count; i++)
        {
            var boneName = boneBindings[i].GetString("BoneName");
            if (boneName is null || !boneIndexByName.TryGetValue(boneName, out var boneIndex))
            {
                throw new InvalidDataException(
                    $"Granny file '{sourceKey}': mesh '{meshName}' bone binding {i} " +
                    $"('{boneName ?? "unnamed"}') has no matching skeleton bone.");
            }

            map[i] = boneIndex;
        }

        return map;
    }

    internal readonly record struct VertexInfluences(
        int BoneIndex0, float Weight0,
        int BoneIndex1, float Weight1,
        int BoneIndex2, float Weight2,
        int BoneIndex3, float Weight3);

    /// <summary>
    /// Aggregates raw four-slot influences onto skeleton bones: file vertices can repeat a
    /// binding across slots, zero weights are dropped, at most four bones are kept (largest
    /// first), and the kept weights are renormalized to sum to one. All-zero vertices fall
    /// back to a full weight on the first bound bone.
    /// </summary>
    internal static VertexInfluences AggregateInfluences(
        int[] bindingToSkeleton,
        byte[] boneIndices,
        byte[] boneWeights,
        int fallbackBoneIndex)
    {
        Dictionary<int, float> weightsByBone = new();
        for (var s = 0; s < boneIndices.Length && s < boneWeights.Length; s++)
        {
            if (boneWeights[s] == 0)
            {
                continue;
            }

            var binding = boneIndices[s];
            if (binding >= bindingToSkeleton.Length)
            {
                continue;
            }

            var bone = bindingToSkeleton[binding];
            weightsByBone.TryGetValue(bone, out var accumulated);
            weightsByBone[bone] = accumulated + boneWeights[s] / 255f;
        }

        if (weightsByBone.Count == 0)
        {
            return new VertexInfluences(fallbackBoneIndex, 1f, 0, 0, 0, 0, 0, 0);
        }

        var kept = weightsByBone.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(4)
            .ToArray();
        var total = kept.Sum(pair => pair.Value);

        var result = new VertexInfluences(
            kept[0].Key, kept[0].Value / total,
            kept.Length > 1 ? kept[1].Key : 0, kept.Length > 1 ? kept[1].Value / total : 0,
            kept.Length > 2 ? kept[2].Key : 0, kept.Length > 2 ? kept[2].Value / total : 0,
            kept.Length > 3 ? kept[3].Key : 0, kept.Length > 3 ? kept[3].Value / total : 0);
        return result;
    }

    private static byte[] ReadInfluenceArray(
        object? value,
        int expectedLength,
        string sourceKey,
        string meshName,
        int vertexIndex,
        string component)
    {
        if (value is byte[] array && array.Length >= expectedLength)
        {
            return array;
        }

        throw new InvalidDataException(
            $"Granny file '{sourceKey}': mesh '{meshName}' vertex {vertexIndex} has no usable " +
            $"{component} component for skinning.");
    }

    private static ushort[] BuildIndexBuffer(
        int[] indices,
        List<Group> groups,
        int groupStart,
        int groupCount,
        bool reverseWinding)
    {
        var total = 0;
        for (var g = 0; g < groupCount; g++)
        {
            total += groups[groupStart + g].TriangleCount * 3;
        }

        var buffer = new ushort[total];
        var write = 0;
        for (var g = 0; g < groupCount; g++)
        {
            var group = groups[groupStart + g];
            for (var t = 0; t < group.TriangleCount; t++)
            {
                for (var c = 0; c < 3; c++)
                {
                    buffer[write++] = (ushort)indices[(group.FirstTriangle + t) * 3 + (reverseWinding ? 2 - c : c)];
                }
            }
        }

        return buffer;
    }

    private static ImportedSubObject CreateSubObject(
        string meshName,
        string sourceKey,
        int subObjectIndex,
        int parentBoneIndex,
        GrannyObject mesh,
        Vector3[] positions,
        Vector3[] normals,
        Vector3?[] tangents,
        Vector2?[]? textureCoordinates,
        ushort[] indices,
        List<Group> groups,
        int groupStart,
        int groupCount,
        List<string?> materialNames,
        Aoe3MaterialConverter materialConverter,
        string materialFileKey,
        bool isSkinned,
        byte[]? skinBoneIndices,
        float[]? skinWeights)
    {
        var boneBindingCount = mesh.GetObjects("BoneBindings").Count;

        var materials = new ImportedMaterial[groupCount];
        var meshParts = new ImportedMeshPart[groupCount];
        for (var g = 0; g < groupCount; g++)
        {
            var group = groups[groupStart + g];
            if (group.MaterialIndex >= materialNames.Count)
            {
                throw new InvalidDataException(
                    $"Granny file '{sourceKey}': mesh '{meshName}' group {g} references material " +
                    $"{group.MaterialIndex}, but only {materialNames.Count} bindings exist.");
            }

            materials[g] = materialConverter.Convert(
                materialNames[group.MaterialIndex], materialFileKey, meshName, boneBindingCount);
            meshParts[g] = new ImportedMeshPart(
                textureCoordinates is null ? null : ExtractCoordinates(textureCoordinates),
                textureCoordinates1: null,
                startIndex: ComputeStartIndex(groups, groupStart, g),
                indexCount: (uint)(group.TriangleCount * 3),
                blendEnabled: false,
                materialIndex: g);
        }

        var vertices = new ImportedVertex[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            if (isSkinned && skinBoneIndices is not null && skinWeights is not null)
            {
                vertices[i] = new ImportedVertex(
                    positions[i],
                    normals[i],
                    tangent: tangents[i],
                    boneIndex0: skinBoneIndices[i * 4 + 0],
                    boneWeight0: skinWeights[i * 4 + 0],
                    boneIndex1: skinBoneIndices[i * 4 + 1],
                    boneWeight1: skinWeights[i * 4 + 1],
                    boneIndex2: skinBoneIndices[i * 4 + 2],
                    boneWeight2: skinWeights[i * 4 + 2],
                    boneIndex3: skinBoneIndices[i * 4 + 3],
                    boneWeight3: skinWeights[i * 4 + 3]);
            }
            else
            {
                vertices[i] = new ImportedVertex(positions[i], normals[i], tangent: tangents[i]);
            }
        }

        var (box, sphere) = ComputeBounds(positions);

        var importedMesh = new ImportedMesh(
            vertices,
            indices,
            box,
            sphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned,
            materials,
            meshParts,
            primitiveId: meshName);

        return new ImportedSubObject(
            $"{sourceKey}.{subObjectIndex}.{meshName}",
            meshName,
            parentBoneIndex,
            importedMesh);
    }

    private static Vector2[]? ExtractCoordinates(Vector2?[] coordinates)
    {
        if (coordinates.Length == 0 || coordinates[0] is null)
        {
            return null;
        }

        var values = new Vector2[coordinates.Length];
        for (var i = 0; i < coordinates.Length; i++)
        {
            values[i] = coordinates[i] ?? Vector2.Zero;
        }

        return values;
    }

    private static uint ComputeStartIndex(List<Group> groups, int groupStart, int offset)
    {
        var start = 0u;
        for (var g = 0; g < offset; g++)
        {
            start += (uint)(groups[groupStart + g].TriangleCount * 3);
        }

        return start;
    }

    private static string BuildMaterialFileKey(string sourceKey, string meshName)
    {
        // Archive keys use backslashes; System.IO path helpers are separator-specific per OS,
        // so the directory is split manually over both separators.
        var lastSeparator = sourceKey.LastIndexOfAny(['\\', '/']);
        var directory = lastSeparator >= 0 ? sourceKey[..lastSeparator] : string.Empty;
        var baseName = meshName.EndsWith("Shape", StringComparison.OrdinalIgnoreCase)
            ? meshName[..^"Shape".Length]
            : meshName;
        return string.IsNullOrEmpty(directory)
            ? baseName + ".material.xmb"
            : directory + "\\" + baseName + ".material.xmb";
    }

    internal static (AxisAlignedBoundingBox Box, BoundingSphere Sphere) ComputeBounds(Vector3[] positions)
    {
        var boundingBox = AxisAlignedBoundingBox.CreateFromPoints(positions);
        var sphereCenter = (boundingBox.Min + boundingBox.Max) / 2.0f;
        var boundingSphere = new BoundingSphere(
            sphereCenter,
            Vector3.Distance(sphereCenter, boundingBox.Max));
        return (boundingBox, boundingSphere);
    }

    private static Vector3 ReadPosition(GrannyObject vertex, string sourceKey, string meshName, int vertexIndex)
    {
        if (vertex.GetArray("Position") is float[] position && position.Length == 3)
        {
            return new Vector3(position[0], position[1], position[2]);
        }

        throw new InvalidDataException(
            $"Granny file '{sourceKey}': mesh '{meshName}' vertex {vertexIndex} has no Position component.");
    }

    private static Vector3? ReadUnitVector(object? value, string sourceKey, string meshName, int vertexIndex, string component)
    {
        switch (value)
        {
            case float[] wide when wide.Length >= 3:
                return Vector3.Normalize(new Vector3(wide[0], wide[1], wide[2]));

            case sbyte[] signed when signed.Length >= 3:
                return Vector3.Normalize(new Vector3(signed[0] / 127f, signed[1] / 127f, signed[2] / 127f));

            case byte[] packed when packed.Length >= 3:
                // NormalUInt8-style packing maps [0, 255] onto [-1, 1].
                return Vector3.Normalize(new Vector3(
                    packed[0] / 127.5f - 1.0f,
                    packed[1] / 127.5f - 1.0f,
                    packed[2] / 127.5f - 1.0f));

            default:
                return null;
        }
    }

    private static Vector2? ReadTextureCoordinates(GrannyObject vertex)
    {
        // Granny stores DCC-convention UVs (origin bottom-left, v up); texture rows run
        // top-first, so v is flipped on import (LSLib's exporter does the same by default).
        if (vertex.GetArray("TextureCoordinates0") is float[] coordinates && coordinates.Length >= 2)
        {
            return new Vector2(coordinates[0], 1f - coordinates[1]);
        }

        return null;
    }
}
