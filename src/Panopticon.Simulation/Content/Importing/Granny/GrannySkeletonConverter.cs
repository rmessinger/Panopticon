#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.Granny;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Converts a Granny Skeleton object into an <see cref="ImportedSkeleton"/> in engine space:
/// local bind TRS per bone, file InverseWorldTransform re-expressed through the axis system.
/// </summary>
internal static class GrannySkeletonConverter
{
    public static ImportedSkeleton Convert(GrannyObject skeleton, GrannyAxisSystem axes, string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        var name = skeleton.GetString("Name") ?? "GrannySkeleton";
        var boneObjects = skeleton.GetObjects("Bones");
        if (boneObjects.Count == 0)
        {
            throw new InvalidDataException($"Granny file '{sourceKey}': skeleton '{name}' has no bones.");
        }

        var bones = new ImportedBone[boneObjects.Count];
        var worldMatrices = new Matrix4x4[boneObjects.Count];

        for (var i = 0; i < boneObjects.Count; i++)
        {
            var boneObject = boneObjects[i];
            var parentIndex = boneObject.GetInt32("ParentIndex") ?? -1;
            var local = ToMatrix(boneObject.GetTransform("Transform"), sourceKey, boneObject, i);

            // Chain file-space worlds (row-vector: local applied first, then the parent chain).
            var worldFile = parentIndex >= 0 ? local * worldMatrices[parentIndex] : local;

            var inverseBindFile = ReadInverseWorldTransform(boneObject, sourceKey, i);
            var inverseBindEngine = axes.ToEngineMatrix(inverseBindFile);

            worldMatrices[i] = worldFile;

            // Bind TRS is the bone's LOCAL transform in engine space; the runtime rebuilds the
            // hierarchy by chaining locals, so storing a world rotation here would double-compose.
            var bindLocal = axes.ToEngineMatrix(local);
            if (!Matrix4x4.Decompose(bindLocal, out _, out var bindRotation, out var bindTranslation))
            {
                throw new InvalidDataException(
                    $"Granny file '{sourceKey}': bind transform of bone {i} is not decomposable.");
            }

            var boneName = boneObject.GetString("Name") ?? $"Bone{i}";
            bones[i] = new ImportedBone(
                boneName,
                parentIndex,
                bindTranslation,
                Quaternion.Normalize(bindRotation),
                inverseBindEngine,
                bindScale: 1f,
                bindLocalMatrix: bindLocal);
        }

        return new ImportedSkeleton(name, bones);
    }

    public static Dictionary<string, int> BuildBoneIndexByName(ImportedSkeleton skeleton)
    {
        var map = new Dictionary<string, int>(skeleton.Bones.Count, StringComparer.Ordinal);
        for (var i = 0; i < skeleton.Bones.Count; i++)
        {
            map[skeleton.Bones[i].Name] = i;
        }

        return map;
    }

    /// <summary>
    /// A Granny Transform packs optional translation, rotation, and scale-shear parts selected
    /// by flag bits; unflagged parts stay identity.
    /// </summary>
    private static Matrix4x4 ToMatrix(GrannyTransform? transform, string sourceKey, GrannyObject bone, int boneIndex)
    {
        if (transform is not { } t)
        {
            throw new InvalidDataException($"Granny file '{sourceKey}': bone {boneIndex} has no Transform.");
        }

        var translation = Vector3.Zero;
        if ((t.Flags & 1u) != 0u)
        {
            translation = ReadVector3(t.Translation);
        }

        var rotation = Quaternion.Identity;
        if ((t.Flags & 2u) != 0u)
        {
            // Stored xyzw; System.Numerics ctor takes xyzw in order x, y, z, w.
            var r = t.Rotation;
            rotation = new Quaternion(r[0], r[1], r[2], r[3]);
        }

        var scale = Matrix4x4.Identity;
        if ((t.Flags & 4u) != 0u)
        {
            // ScaleShear is a row-major 3x3 under Granny's row-vector convention, matching
            // System.Numerics directly.
            var ss = t.ScaleShear;
            scale = new Matrix4x4(
                ss[0], ss[1], ss[2], 0.0f,
                ss[3], ss[4], ss[5], 0.0f,
                ss[6], ss[7], ss[8], 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
        }

        return scale * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }

    private static Matrix4x4 ReadInverseWorldTransform(GrannyObject bone, string sourceKey, int boneIndex)
    {
        if (bone.GetArray("InverseWorldTransform") is not float[] values || values.Length != 16)
        {
            throw new InvalidDataException(
                $"Granny file '{sourceKey}': bone {boneIndex} has no 16-float InverseWorldTransform.");
        }

        // Row-major, row-vector storage in the file, matching System.Numerics directly
        // (translation in the bottom row).
        return new Matrix4x4(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);
    }

    private static Vector3 ReadVector3(float[] values)
    {
        return new Vector3(values[0], values[1], values[2]);
    }
}
