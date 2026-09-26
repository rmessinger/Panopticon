#nullable enable

using System;
using System.Numerics;

namespace Panopticon.Content.Importing.M3;

internal static class M3CoordinateConverter
{
    private static readonly Quaternion SourceToEngineRotation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2.0f);

    public static Vector3 ToEnginePosition(Vector3 source)
    {
        return new Vector3(-10.0f * source.Y, 10.0f * source.X, 10.0f * source.Z);
    }

    public static Vector3 ToEngineDirection(Vector3 source)
    {
        return new Vector3(-source.Y, source.X, source.Z);
    }

    public static Vector3 ToEngineTranslation(Vector3 source)
    {
        return ToEnginePosition(source);
    }

    public static Quaternion ToEngineRotation(Quaternion source)
    {
        var inverse = Quaternion.Inverse(SourceToEngineRotation);
        return Quaternion.Normalize(SourceToEngineRotation * source * inverse);
    }

    public static Vector3 ToEngineScale(Vector3 source)
    {
        return new Vector3(source.Y, source.X, source.Z);
    }

    public static Matrix4x4 ConjugateSourceToEngine(Matrix4x4 source)
    {
        var conversion = CreateSourceToEngineMatrix();
        if (!Matrix4x4.Invert(conversion, out var inverse))
        {
            throw new NotSupportedException("The M3 source-to-engine conversion is not invertible.");
        }

        return inverse * source * conversion;
    }

    internal static Matrix4x4 CreateSourceToEngineMatrix()
    {
        var matrix = Matrix4x4.Identity;
        matrix.M11 = 0.0f;
        matrix.M12 = 10.0f;
        matrix.M21 = -10.0f;
        matrix.M22 = 0.0f;
        matrix.M33 = 10.0f;
        return matrix;
    }
}
