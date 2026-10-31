using System;
using Panopticon.Geospatial.Interchange;
using Xunit;

namespace Panopticon.Geospatial.Tests.Interchange;

public class AffineTransformTests
{
    private static readonly AffineTransform NorthUp = new(100d, 2d, 0d, 500d, 0d, -4d);

    private static readonly AffineTransform Rotated = new(1000d, 2d, 0.5d, 2000d, -0.25d, 3d);

    [Fact]
    public void WorldToPixelInvertsPixelToWorldForNorthUp()
    {
        foreach (var pixel in new[] { new Vector2d(0, 0), new Vector2d(3.5, 7.25), new Vector2d(120.75, 42.5) })
        {
            var roundTrip = NorthUp.WorldToPixel(NorthUp.PixelToWorld(pixel));

            Assert.True(Math.Abs(roundTrip.X - pixel.X) <= 1e-9, $"X {roundTrip.X} differs from {pixel.X}");
            Assert.True(Math.Abs(roundTrip.Y - pixel.Y) <= 1e-9, $"Y {roundTrip.Y} differs from {pixel.Y}");
        }
    }

    [Fact]
    public void WorldToPixelInvertsPixelToWorldForRotationAndShear()
    {
        foreach (var pixel in new[] { new Vector2d(0, 0), new Vector2d(11.5, 2.25), new Vector2d(300.5, 480.75) })
        {
            var roundTrip = Rotated.WorldToPixel(Rotated.PixelToWorld(pixel));

            Assert.True(Math.Abs(roundTrip.X - pixel.X) <= 1e-9, $"X {roundTrip.X} differs from {pixel.X}");
            Assert.True(Math.Abs(roundTrip.Y - pixel.Y) <= 1e-9, $"Y {roundTrip.Y} differs from {pixel.Y}");
        }
    }

    [Fact]
    public void PixelToWorldMapsTopLeftCornerToOrigin()
    {
        var world = NorthUp.PixelToWorld(new Vector2d(0, 0));

        Assert.Equal(100d, world.X);
        Assert.Equal(500d, world.Y);
    }

    [Fact]
    public void PixelToWorldMapsSampleCenterAtHalfPixel()
    {
        var world = NorthUp.PixelToWorld(new Vector2d(0.5, 0.5));

        Assert.Equal(101d, world.X);
        Assert.Equal(498d, world.Y);
    }

    [Fact]
    public void WorldToPixelMapsOriginToZero()
    {
        var pixel = NorthUp.WorldToPixel(new Vector2d(100d, 500d));

        Assert.Equal(0d, pixel.X, 9);
        Assert.Equal(0d, pixel.Y, 9);
    }

    [Fact]
    public void DeterminantIsLinearPartDeterminant()
    {
        Assert.Equal(-8d, NorthUp.Determinant);
        Assert.Equal(6.125d, Rotated.Determinant);
    }

    [Fact]
    public void IsNorthUpRequiresAxisAlignedNegativeRowScale()
    {
        Assert.True(NorthUp.IsNorthUp);
        Assert.False(new AffineTransform(0d, 2d, 0d, 0d, 0d, 4d).IsNorthUp);
        Assert.False(new AffineTransform(0d, 2d, 0.5d, 0d, 0d, -4d).IsNorthUp);
        Assert.False(new AffineTransform(0d, -2d, 0d, 0d, 0d, -4d).IsNorthUp);
    }

    [Fact]
    public void ConstructorRejectsZeroDeterminant()
    {
        Assert.Throws<ArgumentException>(() => new AffineTransform(0d, 0d, 0d, 0d, 0d, -4d));
        Assert.Throws<ArgumentException>(() => new AffineTransform(0d, 2d, 4d, 0d, 2d, 4d));
    }

    [Fact]
    public void ConstructorRejectsOverflowingDeterminant()
    {
        Assert.Throws<ArgumentException>(
            () => new AffineTransform(0d, double.MaxValue, 0d, 0d, 0d, double.MaxValue));
    }

    [Fact]
    public void ConstructorRejectsCancellingDeterminant()
    {
        // Each product is finite-input infinity; infinity minus infinity is NaN.
        Assert.Throws<ArgumentException>(
            () => new AffineTransform(0d, double.MaxValue, double.MaxValue, 0d, double.MaxValue, double.MaxValue));
    }

    [Fact]
    public void ConstructorRejectsNonFiniteCoefficients()
    {
        Assert.Throws<ArgumentException>(() => new AffineTransform(double.NaN, 2d, 0d, 0d, 0d, -4d));
        Assert.Throws<ArgumentException>(() => new AffineTransform(0d, 2d, 0d, 0d, 0d, double.PositiveInfinity));
    }
}
