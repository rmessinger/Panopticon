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
}
