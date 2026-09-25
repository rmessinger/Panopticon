#nullable enable

using System;
using System.Numerics;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Transform math for NIF imports: node-local matrices, hierarchy
/// accumulation, and the single source-to-engine conversion recorded by
/// C4a-01 (right-handed Z-up, -Y forward, meters to upright +X-forward
/// feet): Rz(+90 degrees) with a uniform meters-to-feet scale.
/// </summary>
internal static class NifTransformConverter
{
    public const float MetersPerFoot = 0.3048f;
    public const float FeetPerMeter = 1.0f / MetersPerFoot;

    private static readonly Matrix4x4 SourceToEngine = CreateSourceToEngine();

    private static Matrix4x4 CreateSourceToEngine()
    {
        // e_x = -n_y, e_y = n_x, e_z = n_z (row-vector convention),
        // times a uniform meters-to-feet scale.
        var m = Matrix4x4.Identity;
        m.M11 = 0.0f * FeetPerMeter;
        m.M12 = 1.0f * FeetPerMeter;
        m.M13 = 0.0f * FeetPerMeter;
        m.M21 = -1.0f * FeetPerMeter;
        m.M22 = 0.0f * FeetPerMeter;
        m.M23 = 0.0f * FeetPerMeter;
        m.M31 = 0.0f * FeetPerMeter;
        m.M32 = 0.0f * FeetPerMeter;
        m.M33 = 1.0f * FeetPerMeter;
        return m;
    }

    /// <summary>
    /// Builds one node's local matrix in row-vector convention (T * R * S)
    /// and rejects non-finite or degenerate values. The stored NIF 3x3 is
    /// the column-vector map, so it is transposed into the row-vector
    /// engine layout; anchored by the galley oar rest poses (a positional
    /// copy rendered oars under the keel, the transpose renders them
    /// outboard).
    /// </summary>
    internal static Matrix4x4 CreateLocalMatrix(FileFormats.Nif.NifAvObject node)
    {
        if (!IsFinite(node.Translation) ||
            !IsFinite(node.Scale) ||
            !IsFinite(node.Rotation))
        {
            throw new NotSupportedException(
                $"NIF node '{node.Name}' has a non-finite local transform.");
        }

        var rotation = node.Rotation;
        var scale = node.Scale;
        var m = Matrix4x4.Identity;
        m.M11 = rotation.M11 * scale;
        m.M12 = rotation.M21 * scale;
        m.M13 = rotation.M31 * scale;
        m.M21 = rotation.M12 * scale;
        m.M22 = rotation.M22 * scale;
        m.M23 = rotation.M32 * scale;
        m.M31 = rotation.M13 * scale;
        m.M32 = rotation.M23 * scale;
        m.M33 = rotation.M33 * scale;
        m.M41 = node.Translation.X;
        m.M42 = node.Translation.Y;
        m.M43 = node.Translation.Z;
        return m;
    }

    /// <summary>
    /// Composes a child's world matrix: the child's local matrix applies
    /// first, then the parent's world (row-vector order).
    /// </summary>
    internal static Matrix4x4 ComposeWorld(Matrix4x4 local, Matrix4x4 parentWorld)
    {
        return local * parentWorld;
    }

    /// <summary>
    /// One node's engine-frame bone-local transform: the source local
    /// conjugated by the source-to-engine map. The rotation conjugates by
    /// Rz(90) with the uniform node scale cancelling; the translation is
    /// rotated by Rz(90) and converted to feet.
    /// </summary>
    internal static Matrix4x4 CreateBoneLocalMatrix(FileFormats.Nif.NifAvObject node)
    {
        return ConjugateSourceToEngine(CreateLocalMatrix(node));
    }

    /// <summary>
    /// Conjugates a source-space transform by the source-to-engine map:
    /// E^-1 * source * E (row-vector products). Conjugated locals compose
    /// exactly: conjugating an accumulated source chain equals
    /// accumulating the conjugated locals.
    /// </summary>
    internal static Matrix4x4 ConjugateSourceToEngine(in Matrix4x4 source)
    {
        if (!Matrix4x4.Invert(SourceToEngine, out var inverse))
        {
            throw new NotSupportedException(
                "The NIF source-to-engine conversion is not invertible.");
        }

        return inverse * source * SourceToEngine;
    }

    /// <summary>
    /// Converts a source-frame rotation to the engine frame by
    /// conjugation, with the same negative-W canonicalization as the
    /// glTF converter.
    /// </summary>
    internal static Quaternion ConjugateSourceRotationToEngine(Quaternion rotation)
    {
        var source = Matrix4x4.CreateFromQuaternion(rotation);
        var engine = ConjugateSourceToEngine(source);
        var result = Quaternion.CreateFromRotationMatrix(engine);
        return result.W < 0f ? Quaternion.Negate(result) : result;
    }

    /// <summary>
    /// Converts a source-frame translation to the engine frame: rotated
    /// by Rz(90 degrees) and scaled from meters to feet.
    /// </summary>
    internal static Vector3 ConvertSourceTranslationToEngine(Vector3 translation)
    {
        return Vector3.Transform(translation, SourceToEngine);
    }

    /// <summary>
    /// The full position bake for one shape: accumulated world transform
    /// followed by the source-to-engine conversion.
    /// </summary>
    internal static Matrix4x4 CreateBakeMatrix(Matrix4x4 world)
    {
        return world * SourceToEngine;
    }

    /// <summary>
    /// The determinant of the bake's linear part. Zero is degenerate and
    /// rejected; negative means the winding must be reversed.
    /// </summary>
    internal static float BakeDeterminant(Matrix4x4 bake)
    {
        return bake.M11 * (bake.M22 * bake.M33 - bake.M23 * bake.M32)
             - bake.M12 * (bake.M21 * bake.M33 - bake.M23 * bake.M31)
             + bake.M13 * (bake.M21 * bake.M32 - bake.M22 * bake.M31);
    }

    /// <summary>
    /// Transforms normals by the inverse transpose of the bake's linear
    /// part and normalizes, so non-uniform or mirrored transforms keep
    /// normals valid.
    /// </summary>
    internal static Vector3 TransformNormal(Vector3 normal, Matrix4x4 bake)
    {
        if (!Matrix4x4.Invert(bake, out var inverse))
        {
            throw new NotSupportedException(
                "NIF transform bake is not invertible; normals cannot be baked safely.");
        }

        var normalMatrix = Matrix4x4.Transpose(inverse);
        return Vector3.Normalize(Vector3.TransformNormal(normal, normalMatrix));
    }

    internal static Vector3 TransformPosition(Vector3 position, Matrix4x4 bake)
    {
        return Vector3.Transform(position, bake);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    private static bool IsFinite(float value) => float.IsFinite(value);

    private static bool IsFinite(FileFormats.Nif.NifMatrix33 value)
    {
        return float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13)
            && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23)
            && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33);
    }
}
