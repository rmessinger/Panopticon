using System;
using System.Numerics;
using Xunit;

namespace Panopticon.Mathematics.Tests;

public class Matrix4x4UtilityTests
{
    [Theory]
    [InlineData(3f, 4f, 0f)]
    [InlineData(1f, 0f, 1f)]
    [InlineData(1f, 0f, -1f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(0f, 0f, -1f)]
    public void TryCreateTransformMatrix_AlignsLocalXAxisToDirection(
        float directionX,
        float directionY,
        float directionZ)
    {
        var position = new Vector3(10f, 20f, 30f);
        var direction = new Vector3(directionX, directionY, directionZ);

        var created = Matrix4x4Utility.TryCreateTransformMatrix(
            position,
            direction,
            2.5f,
            out var transform);

        Assert.True(created);
        Assert.Equal(position, transform.Translation);
        AssertDirection(Vector3.Normalize(direction), GetUnitXAxis(transform));
        Assert.Equal(2.5f, transform.GetXVector().Length(), 5);
        Assert.Equal(2.5f, transform.GetYVector().Length(), 5);
        Assert.Equal(2.5f, transform.GetZVector().Length(), 5);
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(float.PositiveInfinity, 0f, 0f)]
    public void TryCreateTransformMatrix_RejectsInvalidDirection(
        float directionX,
        float directionY,
        float directionZ)
    {
        var created = Matrix4x4Utility.TryCreateTransformMatrix(
            Vector3.Zero,
            new Vector3(directionX, directionY, directionZ),
            1.0f,
            out _);

        Assert.False(created);
    }

    private static Vector3 GetUnitXAxis(in Matrix4x4 transform) =>
        Vector3.Normalize(transform.GetXVector());

    private static void AssertDirection(in Vector3 expected, in Vector3 actual)
    {
        Assert.InRange(Vector3.Dot(expected, actual), 0.99999f, 1.00001f);
    }
}
