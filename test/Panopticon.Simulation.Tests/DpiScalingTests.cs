using System.Numerics;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests;

public class DpiScalingTests
{
    [Fact]
    public void GetScale_IsOne_WhenPixelAndLogicalWidthsMatch()
    {
        Assert.Equal(1.0f, DpiScaling.GetScale(1024, 1024));
    }

    [Fact]
    public void GetScale_IsPixelsPerPoint()
    {
        Assert.Equal(2.0f, DpiScaling.GetScale(2048, 1024));
        Assert.Equal(1.5f, DpiScaling.GetScale(1536, 1024));
    }

    [Theory]
    [InlineData(1024, 0)]
    [InlineData(0, 1024)]
    [InlineData(0, 0)]
    public void GetScale_IsOne_ForZeroWidths(int pixelWidth, int logicalWidth)
    {
        Assert.Equal(1.0f, DpiScaling.GetScale(pixelWidth, logicalWidth));
    }

    [Fact]
    public void ToPixels_IsIdentity_AtScaleOne()
    {
        Assert.Equal(new Point2D(123, 45), DpiScaling.ToPixels(new Point2D(123, 45), 1.0f));
        Assert.Equal(new Rectangle(10, 20, 300, 400), DpiScaling.ToPixels(new Rectangle(10, 20, 300, 400), 1.0f));
    }

    [Fact]
    public void ToPixels_MultipliesByScale()
    {
        Assert.Equal(new Point2D(246, 90), DpiScaling.ToPixels(new Point2D(123, 45), 2.0f));
        Assert.Equal(new Rectangle(20, 40, 600, 800), DpiScaling.ToPixels(new Rectangle(10, 20, 300, 400), 2.0f));
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void ToPixels_KeepsNormalizedPosition(float scale)
    {
        var logical = new Size(1024, 768);
        var drawable = new Size((int)(logical.Width * scale), (int)(logical.Height * scale));

        var pixel = DpiScaling.ToPixels(new Point2D(logical.Width / 2, logical.Height / 2), scale);

        Assert.Equal(0.5f, pixel.X / (float)drawable.Width);
        Assert.Equal(0.5f, pixel.Y / (float)drawable.Height);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    public void WndLayoutOnPixels_MapsConvertedMouseToSameClientPoint(float scale)
    {
        // WND windows fit their 800x600 creation resolution to the panel size, so a GUI element
        // keeps the same fraction of the screen whether the panel is sized in points or pixels.
        var creationResolution = new SizeF(800, 600);
        var bounds = new RectangleF(0, 0, 800, 600);
        var logical = new Size(1024, 768);
        var drawable = new Size((int)(logical.Width * scale), (int)(logical.Height * scale));

        var transform = RectangleF.CalculateTransformForRectangleFittingAspectRatio(bounds, creationResolution, drawable);
        Matrix3x2.Invert(transform, out var inverse);

        var mouse = DpiScaling.ToPixels(new Point2D(512, 384), scale);
        var client = Point2D.Transform(mouse, inverse);

        Assert.Equal(new Point2D(400, 300), client);
    }
}
