using System;
using System.Numerics;
using Panopticon.Content.Importing.M3;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class M3CoordinateConverterTests
{
    [Fact]
    public void PositionsAndDirectionsMapToEngineFrame()
    {
        AssertEqual(new Vector3(10, 0, 0), M3CoordinateConverter.ToEnginePosition(new Vector3(0, -1, 0)));
        AssertEqual(new Vector3(0, 0, 10), M3CoordinateConverter.ToEnginePosition(Vector3.UnitZ));
        AssertEqual(new Vector3(10, 0, 0), M3CoordinateConverter.ToEngineDirection(new Vector3(0, -10, 0)));
        AssertEqual(Vector3.UnitZ, M3CoordinateConverter.ToEngineDirection(Vector3.UnitZ));
    }

    [Fact]
    public void RotationConjugationAndScaleMatchMatrixConjugation()
    {
        var sourceRotation = Quaternion.Normalize(
            Quaternion.CreateFromYawPitchRoll(0.31f, -0.23f, 0.47f));
        var sourceScale = new Vector3(2, 3, 4);
        var convertedRotation = M3CoordinateConverter.ToEngineRotation(sourceRotation);
        var convertedScale = M3CoordinateConverter.ToEngineScale(sourceScale);
        AssertEqual(new Vector3(3, 2, 4), convertedScale);

        var sourceMatrix =
            Matrix4x4.CreateScale(sourceScale) *
            Matrix4x4.CreateFromQuaternion(sourceRotation) *
            Matrix4x4.CreateTranslation(new Vector3(1, 2, 3));
        var convertedMatrix = M3CoordinateConverter.ConjugateSourceToEngine(sourceMatrix);
        var expectedMatrix =
            Matrix4x4.CreateScale(convertedScale) *
            Matrix4x4.CreateFromQuaternion(convertedRotation) *
            Matrix4x4.CreateTranslation(M3CoordinateConverter.ToEngineTranslation(new Vector3(1, 2, 3)));

        AssertMatrixEqual(expectedMatrix, convertedMatrix, 1e-5f);
    }

    private static void AssertEqual(Vector3 expected, Vector3 actual, float tolerance = 1e-5f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"Expected {expected}, got {actual}.");
    }

    private static void AssertMatrixEqual(Matrix4x4 expected, Matrix4x4 actual, float tolerance)
    {
        var expectedValues = Values(expected);
        var actualValues = Values(actual);
        for (var i = 0; i < expectedValues.Length; i++)
        {
            Assert.True(
                MathF.Abs(expectedValues[i] - actualValues[i]) <= tolerance,
                $"Matrix element {i}: expected {expectedValues[i]}, got {actualValues[i]}.");
        }
    }

    private static float[] Values(Matrix4x4 matrix)
    {
        return
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        ];
    }
}
