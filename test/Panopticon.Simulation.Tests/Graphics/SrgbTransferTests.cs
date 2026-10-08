using System;
using Veldrid.ImageSharp;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class SrgbTransferTests
{
    [Fact]
    public void EndpointsAreExact()
    {
        Assert.Equal(0f, SrgbTransfer.ToLinear(0f));
        Assert.Equal(1f, SrgbTransfer.ToLinear(1f), 5);
        Assert.Equal(0f, SrgbTransfer.ToSrgb(0f));
        Assert.Equal(1f, SrgbTransfer.ToSrgb(1f), 5);
    }

    [Fact]
    public void MidGreyDecodesToLinearQuarter()
    {
        // The sRGB curve is piecewise, not gamma 2.2: encoded 0.5 is ~0.214 linear.
        Assert.Equal(0.2140f, SrgbTransfer.ToLinear(0.5f), 3);
    }

    [Fact]
    public void LinearMidGreyEncodesToByte188()
    {
        // Filtering 50% black/white coverage in linear light must average near 188,
        // not the 128 an encoded-space filter produces.
        Assert.Equal(188, SrgbTransfer.ToSrgbByte(0.5f));
    }

    [Fact]
    public void RoundTripStaysWithinOneCodeStep()
    {
        for (var i = 0; i <= 255; i += 5)
        {
            var encoded = (float)i / 255;
            var roundTripped = SrgbTransfer.ToSrgb(SrgbTransfer.ToLinear(encoded));

            Assert.True(Math.Abs(roundTripped - encoded) <= 1f / 255, $"code {i} round tripped to {roundTripped * 255}");
        }
    }

    [Fact]
    public void LinearRoundTripStaysWithinOneCodeStep()
    {
        for (var i = 0; i <= 255; i += 5)
        {
            var linear = (float)i / 255;
            var roundTripped = SrgbTransfer.ToLinear(SrgbTransfer.ToSrgb(linear));

            Assert.True(Math.Abs(roundTripped - linear) <= 1f / 255, $"linear {i} round tripped to {roundTripped * 255}");
        }
    }
}
