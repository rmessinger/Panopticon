using System;
using Panopticon.Graphics;
using Veldrid;
using Xunit;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Tests.Graphics;

public class TemporalImageQualityMetricsTests
{
    private static readonly Rectangle Region = new(0, 0, 2, 1);

    [Fact]
    public void MatchingCameraMotion_HasNoTemporalError()
    {
        var previous = Frame(0, 255);
        var current = Frame(255, 0);
        Assert.Equal(0, TemporalImageQualityMetrics.Score(previous, current, previous, current, Region));
    }

    [Fact]
    public void AlternatingSamplingError_IsDetected()
    {
        var reference = Frame(128, 128);
        var score = TemporalImageQualityMetrics.Score(Frame(0, 255), Frame(255, 0), reference, reference, Region);
        Assert.Equal(0.5, score, 6);
    }

    [Fact]
    public void StableError_DoesNotCountAsShimmer()
    {
        Assert.Equal(0, TemporalImageQualityMetrics.Score(Frame(10, 20), Frame(20, 30), Frame(0, 10), Frame(10, 20), Region), 12);
    }

    [Fact]
    public void Region_ExcludesUnrelatedMotion()
    {
        var reference = Frame(128, 128);
        Assert.Equal(0, TemporalImageQualityMetrics.Score(Frame(128, 0), Frame(128, 255), reference, reference, new Rectangle(0, 0, 1, 1)));
    }

    [Fact]
    public void BgraAndRgba_AreEquivalent()
    {
        var rgba = new CapturedFrame([255, 0, 0, 255], 1, 1, PixelFormat.R8_G8_B8_A8_UNorm);
        var bgra = new CapturedFrame([0, 0, 255, 255], 1, 1, PixelFormat.B8_G8_R8_A8_UNorm);
        Assert.Equal(0, TemporalImageQualityMetrics.Score(rgba, bgra, rgba, rgba, new Rectangle(0, 0, 1, 1)));
    }

    [Fact]
    public void InvalidDimensionsRegionAndBuffer_AreRejected()
    {
        var valid = Frame(0, 0);
        var narrow = new CapturedFrame([0, 0, 0, 255], 1, 1, PixelFormat.R8_G8_B8_A8_UNorm);
        Assert.Throws<ArgumentException>(() => TemporalImageQualityMetrics.Score(valid, valid, valid, narrow, new Rectangle(0, 0, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TemporalImageQualityMetrics.Score(valid, valid, valid, valid, new Rectangle(0, 0, 0, 1)));
        Assert.Throws<ArgumentException>(() => TemporalImageQualityMetrics.Score(valid, valid, valid,
            new CapturedFrame([], 2, 1, PixelFormat.R8_G8_B8_A8_UNorm), Region));
    }

    [Fact]
    public void MaximumAlternatingError_IsNormalizedToOne()
    {
        Assert.Equal(1, TemporalImageQualityMetrics.Score(Frame(0, 0), Frame(255, 255),
            Frame(255, 255), Frame(0, 0), Region), 12);
    }

    [Fact]
    public void UnsupportedFormatAndNull_AreRejected()
    {
        var valid = Frame(0, 0);
        Assert.Throws<ArgumentNullException>(() => TemporalImageQualityMetrics.Score(null!, valid, valid, valid, Region));
        Assert.Throws<NotSupportedException>(() => TemporalImageQualityMetrics.Score(valid, valid, valid,
            new CapturedFrame(new byte[8], 2, 1, PixelFormat.R16_UNorm), Region));
    }

    private static CapturedFrame Frame(byte left, byte right) => new(
        [left, left, left, 255, right, right, right, 255], 2, 1, PixelFormat.R8_G8_B8_A8_UNorm);
}
