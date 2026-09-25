using System;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// Row-major 3x3 rotation matrix as stored by NIF.
/// Parser value only; conversion to interchange types belongs to the importer.
/// </summary>
public readonly record struct NifMatrix33(
    float M11, float M12, float M13,
    float M21, float M22, float M23,
    float M31, float M32, float M33)
{
    public static NifMatrix33 Identity { get; } = new(
        1, 0, 0,
        0, 1, 0,
        0, 0, 1);

    public bool IsIdentity(float epsilon)
    {
        return MathF.Abs(M11 - 1) <= epsilon && MathF.Abs(M12) <= epsilon && MathF.Abs(M13) <= epsilon
            && MathF.Abs(M21) <= epsilon && MathF.Abs(M22 - 1) <= epsilon && MathF.Abs(M23) <= epsilon
            && MathF.Abs(M31) <= epsilon && MathF.Abs(M32) <= epsilon && MathF.Abs(M33 - 1) <= epsilon;
    }

    public float Determinant =>
        M11 * (M22 * M33 - M23 * M32)
        - M12 * (M21 * M33 - M23 * M31)
        + M13 * (M21 * M32 - M22 * M31);
}
