#nullable enable

using System;
using System.Numerics;
using Panopticon;

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

    /// <summary>Uniform bind-pose scale; defaults to 1 for formats without stored scale.</summary>
    public float BindScale { get; }

    /// <summary>Exact local bind transform, including non-uniform scale and shear.</summary>
    public Matrix4x4 BindLocalMatrix { get; }

    internal bool HasExactBindLocalMatrix { get; }

    public Matrix4x4 InverseBindMatrix { get; }

    public ImportedBone(string name, int parentIndex, in Vector3 bindTranslation, in Quaternion bindRotation)
        : this(name, parentIndex, bindTranslation, bindRotation, Matrix4x4.Identity)
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
        float bindScale)
        : this(name, parentIndex, bindTranslation, bindRotation, Matrix4x4.Identity, bindScale)
    {
    }

    public ImportedBone(
        string name,
        int parentIndex,
        in Vector3 bindTranslation,
        in Quaternion bindRotation,
        in Matrix4x4 inverseBindMatrix,
        float bindScale,
        Matrix4x4? bindLocalMatrix = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        Ensure.AtLeast(parentIndex, -1, "Parent index must be -1 (root) or a bone index.");

        if (!float.IsFinite(bindScale) || bindScale < 0.0f)
        {
            throw new ArgumentException("Bind scale must be finite and non-negative.", nameof(bindScale));
        }

        Name = name;
        ParentIndex = parentIndex;
        BindTranslation = bindTranslation;
        BindRotation = bindRotation;
        BindScale = bindScale;
        HasExactBindLocalMatrix = bindLocalMatrix.HasValue;
        BindLocalMatrix = bindLocalMatrix ?? Matrix4x4.CreateScale(bindScale)
            * Matrix4x4.CreateFromQuaternion(bindRotation) * Matrix4x4.CreateTranslation(bindTranslation);
        InverseBindMatrix = inverseBindMatrix;
    }
}
