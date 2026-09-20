#nullable enable

using System.Numerics;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Row-vector basis conversion from glTF (right-handed, Y-up, +Z asset front) into OpenSAGE
/// model space (right-handed, Z-up, +X forward): (x, y, z) glTF to (z, x, y) OpenSAGE, scaled
/// by units per meter. The permutation is a 3-cycle, so winding is preserved.
/// </summary>
internal static class GltfTransformConverter
{
    public static Matrix4x4 CreateBasis(float unitsPerMeter)
    {
        return new Matrix4x4(
            0, unitsPerMeter, 0, 0,
            0, 0, unitsPerMeter, 0,
            unitsPerMeter, 0, 0, 0,
            0, 0, 0, 1);
    }

    public static Matrix4x4 CreateUnscaledBasis() => CreateBasis(1f);

    public static Vector3 ConvertPosition(in Vector3 position, in Matrix4x4 basis)
    {
        return Vector3.Transform(position, basis);
    }

    public static Vector3 ConvertDirection(in Vector3 direction)
    {
        return Vector3.Transform(direction, CreateUnscaledBasis());
    }

    /// <summary>
    /// Converts one glTF local transform matrix: M' = C^-1 * M * C under the repository's
    /// row-vector convention, so converted points transform identically.
    /// </summary>
    public static Matrix4x4 ConvertMatrix(in Matrix4x4 matrix, in Matrix4x4 basis)
    {
        Matrix4x4.Invert(basis, out var inverseBasis);
        return inverseBasis * matrix * basis;
    }

    public static Quaternion ConvertRotation(in Quaternion rotation, in Matrix4x4 basis)
    {
        var matrix = Matrix4x4.CreateFromQuaternion(rotation);
        var converted = ConvertMatrix(matrix, basis);
        var result = Quaternion.CreateFromRotationMatrix(converted);

        // Canonicalize the sign so equivalent input quaternions convert deterministically.
        return result.W < 0
            ? Quaternion.Negate(result)
            : result;
    }
}
