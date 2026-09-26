#nullable enable

using System;
using System.Numerics;

namespace Panopticon.Content.Importing;

/// <summary>
/// One skeleton bone: name, parent index (-1 for a root), bind pose transform, and optional
/// inverse bind matrix (identity for formats without one, e.g. W3D).
/// </summary>
public sealed class ImportedBone
{
    public string Name { get; }

    public int ParentIndex { get; }

    public Vector3 BindTranslation { get; }

    public Quaternion BindRotation { get; }

    public Vector3 BindScale { get; }

    public Matrix4x4 InverseBindMatrix { get; }

    public ImportedBone(string name, int parentIndex, in Vector3 bindTranslation, in Quaternion bindRotation)
        : this(name, parentIndex, bindTranslation, bindRotation, Matrix4x4.Identity, bindScale: 1.0f)
    {
    }

    public ImportedBone(
        string name,
        int parentIndex,
        in Vector3 bindTranslation,
        in Quaternion bindRotation,
        float bindScale)
        : this(name, parentIndex, bindTranslation, bindRotation, Matrix4x4.Identity, new Vector3(bindScale))
    {
    }

    public ImportedBone(
        string name,
        int parentIndex,
        in Vector3 bindTranslation,
        in Quaternion bindRotation,
        in Vector3 bindScale)
        : this(name, parentIndex, bindTranslation, bindRotation, Matrix4x4.Identity, bindScale)
    {
    }

    public ImportedBone(
        string name,
        int parentIndex,
        in Vector3 bindTranslation,
        in Quaternion bindRotation,
        in Matrix4x4 inverseBindMatrix)
        : this(name, parentIndex, bindTranslation, bindRotation, inverseBindMatrix, bindScale: 1.0f)
    {
    }

    public ImportedBone(
        string name,
        int parentIndex,
        in Vector3 bindTranslation,
        in Quaternion bindRotation,
        in Matrix4x4 inverseBindMatrix,
        float bindScale)
        : this(name, parentIndex, bindTranslation, bindRotation, inverseBindMatrix, new Vector3(bindScale))
    {
    }

    public ImportedBone(
        string name,
        int parentIndex,
        in Vector3 bindTranslation,
        in Quaternion bindRotation,
        in Matrix4x4 inverseBindMatrix,
        in Vector3 bindScale)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (parentIndex < -1)
        {
            throw new ArgumentException("Parent index must be -1 (root) or a bone index.", nameof(parentIndex));
        }

        if (!float.IsFinite(bindScale.X) ||
            !float.IsFinite(bindScale.Y) ||
            !float.IsFinite(bindScale.Z) ||
            bindScale.X <= 0.0f ||
            bindScale.Y <= 0.0f ||
            bindScale.Z <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(bindScale), "Bind scale must be finite and positive on every axis.");
        }

        Name = name;
        ParentIndex = parentIndex;
        BindTranslation = bindTranslation;
        BindRotation = bindRotation;
        BindScale = bindScale;
        InverseBindMatrix = inverseBindMatrix;
    }
}
