using System;
using System.Diagnostics;
using System.Numerics;

namespace OpenSage.Mathematics;

public static class Matrix4x4Utility
{
    public static Matrix4x4 Invert(in Matrix4x4 value)
    {
        if (!Matrix4x4.Invert(value, out var result))
        {
            throw new System.InvalidOperationException();
        }
        return result;
    }

    public static float GetXTranslation(in this Matrix4x4 value) => value.M41;

    public static float GetYTranslation(in this Matrix4x4 value) => value.M42;

    public static float GetZTranslation(in this Matrix4x4 value) => value.M43;

    public static float GetZRotation(in this Matrix4x4 matrix) => MathF.Atan2(matrix.M12, matrix.M11);

    public static void SetXTranslation(ref this Matrix4x4 matrix, float value) => matrix.M41 = value;

    public static void SetYTranslation(ref this Matrix4x4 matrix, float value) => matrix.M42 = value;

    public static void SetZTranslation(ref this Matrix4x4 matrix, float value) => matrix.M43 = value;

    public static Vector3 GetXVector(in this Matrix4x4 value)
    {
        return new Vector3(value.M11, value.M12, value.M13);
    }

    public static Vector3 GetYVector(in this Matrix4x4 value)
    {
        return new Vector3(value.M21, value.M22, value.M23);
    }

    public static Vector3 GetZVector(in this Matrix4x4 value)
    {
        return new Vector3(value.M31, value.M32, value.M33);
    }

    /// <summary>
    /// The backward vector formed from the third row M31, M32, M33 elements.
    /// </summary>
    public static Vector3 Backward(in this Matrix4x4 value)
    {
        return new Vector3(value.M31, value.M32, value.M33);
    }

    /// <summary>
    /// The right vector formed from the first row M11, M12, M13 elements.
    /// </summary>
    public static Vector3 Right(in this Matrix4x4 value)
    {
        return new Vector3(value.M11, value.M12, value.M13);
    }

    /// <summary>
    /// The up vector formed from the second row M21, M22, M23 elements.
    /// </summary>
    public static Vector3 Up(in this Matrix4x4 value)
    {
        return new Vector3(value.M21, value.M22, value.M23);
    }

    public static void ToMatrix4x3(in this Matrix4x4 value, out Matrix4x3 output)
    {
        output = new Matrix4x3(
            value.M11,
            value.M12,
            value.M13,

            value.M21,
            value.M22,
            value.M23,

            value.M31,
            value.M32,
            value.M33,

            value.M41,
            value.M42,
            value.M43);
    }

    public static void RotateX(ref this Matrix4x4 value, float theta)
    {
        float tmp1, tmp2;

        var s = MathF.Sin(theta);
        var c = MathF.Cos(theta);

        tmp1 = value.M21;
        tmp2 = value.M31;

        value.M21 = c * tmp1 + s * tmp2;
        value.M31 = -s * tmp1 + c * tmp2;

        tmp1 = value.M22;
        tmp2 = value.M32;

        value.M22 = c * tmp1 + s * tmp2;
        value.M32 = -s * tmp1 + c * tmp2;

        tmp1 = value.M23;
        tmp2 = value.M33;

        value.M23 = c * tmp1 + s * tmp2;
        value.M33 = -s * tmp1 + c * tmp2;
    }

    public static void RotateY(ref this Matrix4x4 value, float theta)
    {
        var s = MathF.Sin(theta);
        var c = MathF.Cos(theta);

        value.RotateY(s, c);
    }

    public static void RotateY(ref this Matrix4x4 value, float s, float c)
    {
        float tmp1, tmp2;

        tmp1 = value.M11;
        tmp2 = value.M31;

        value.M11 = c * tmp1 - s * tmp2;
        value.M31 = s * tmp1 + c * tmp2;

        tmp1 = value.M12;
        tmp2 = value.M32;

        value.M12 = c * tmp1 - s * tmp2;
        value.M32 = s * tmp1 + c * tmp2;

        tmp1 = value.M13;
        tmp2 = value.M33;

        value.M13 = c * tmp1 - s * tmp2;
        value.M33 = s * tmp1 + c * tmp2;
    }

    public static void RotateZ(ref this Matrix4x4 value, float theta)
    {
        var s = MathF.Sin(theta);
        var c = MathF.Cos(theta);

        value.RotateZ(s, c);
    }

    public static void RotateZ(ref this Matrix4x4 value, float s, float c)
    {
        float tmp1, tmp2;

        tmp1 = value.M11;
        tmp2 = value.M21;

        value.M11 = c * tmp1 + s * tmp2;
        value.M21 = -s * tmp1 + c * tmp2;

        tmp1 = value.M12;
        tmp2 = value.M22;

        value.M12 = c * tmp1 + s * tmp2;
        value.M22 = -s * tmp1 + c * tmp2;

        tmp1 = value.M13;
        tmp2 = value.M23;

        value.M13 = c * tmp1 + s * tmp2;
        value.M23 = -s * tmp1 + c * tmp2;
    }

    /// <summary>
    /// Creates a matrix given a position and a direction (x axis will point in direction).
    /// Direction vector must be normalized!
    /// </summary>
    public static Matrix4x4 CreateTransformMatrix(in Vector3 pos, in Vector3 dir, float scale = 1.0f)
    {
        // Tolerance instead of exact equality: a vector that was normalized once can differ
        // from its own re-normalization by an ulp. Also fails for NaN/zero directions.
        Debug.Assert(MathF.Abs(dir.Length() - 1.0f) < 0.001f);
        Debug.Assert(float.IsFinite(scale));

        // Rows are the basis vectors, because Matrix4x4 here is row-vector (v * M): row 1 is
        // what the local +X axis maps to, so it must be `dir` exactly. Building the basis
        // directly avoids the yaw/pitch decomposition, which is easy to compose in the wrong
        // order and then feeds a wrong forward vector back into the next frame's steering.
        var lateral = new Vector3(-dir.Y, dir.X, 0.0f);
        var lateralLength = lateral.Length();
        lateral = lateralLength > 0.0001f
            ? lateral / lateralLength
            : Vector3.UnitY; // Straight up or down: any perpendicular will do.

        var up = Vector3.Cross(dir, lateral);

        var result = new Matrix4x4(
            dir.X * scale, dir.Y * scale, dir.Z * scale, 0.0f,
            lateral.X * scale, lateral.Y * scale, lateral.Z * scale, 0.0f,
            up.X * scale, up.Y * scale, up.Z * scale, 0.0f,
            pos.X, pos.Y, pos.Z, 1.0f);

        return result;
    }

    /// <summary>
    /// Tries to create a transform whose local +X axis follows an arbitrary direction.
    /// </summary>
    public static bool TryCreateTransformMatrix(
        in Vector3 pos,
        in Vector3 direction,
        float scale,
        out Matrix4x4 result)
    {
        var lengthSquared = direction.LengthSquared();
        if (!(lengthSquared > 1e-6f)
            || !float.IsFinite(lengthSquared)
            || !float.IsFinite(scale))
        {
            result = default;
            return false;
        }

        var normalizedDirection = direction / MathF.Sqrt(lengthSquared);
        result = CreateTransformMatrix(pos, normalizedDirection, scale);
        return true;
    }
}
