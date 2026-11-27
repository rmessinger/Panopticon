#nullable enable

using System;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// Pure mathematics coverage for one generated-bridge approach ramp: length selection,
/// endpoint continuity, cross-blend clamping, quantization and validation.
/// </summary>
public sealed class GeneratedBridgeApproachProfileTests
{
    private const double TightTolerance = 1e-9;
    private const double GradeTolerance = 1e-4;

    [Fact]
    public void TryCreate_SouthFixture_ReturnsShortestQualifyingLength()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 260, out var unsupportedReason);

        Assert.NotNull(profile);
        Assert.Null(unsupportedReason);
        Assert.Equal(260d, profile.Length, TightTolerance);
        Assert.Equal(-300d, profile.ToeS, TightTolerance);
        Assert.Equal(1068.75, profile.ToeHeight, TightTolerance);
        Assert.Equal(-0.05, profile.ToeSlope, TightTolerance);
        Assert.Equal(0.17604, profile.DesignMaxGrade, GradeTolerance);
        Assert.True(profile.DesignMaxGrade > 0.17);
    }

    [Fact]
    public void TryCreate_SouthFixtureWithShorterMaximum_ReturnsNull()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 250, out var unsupportedReason);

        Assert.Null(profile);
        Assert.NotNull(unsupportedReason);
    }

    [Fact]
    public void TryCreate_NorthFixture_ReturnsMirroredProfile()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var profile = GeneratedBridgeApproachProfile.TryCreate(740, false, 1101.25, nativeCentreHeight, 10, 260, out var unsupportedReason);

        Assert.NotNull(profile);
        Assert.Null(unsupportedReason);
        Assert.Equal(260d, profile.Length, TightTolerance);
        Assert.Equal(1000d, profile.ToeS, TightTolerance);
        Assert.Equal(1133.75, profile.ToeHeight, TightTolerance);
        Assert.Equal(0.05, profile.ToeSlope, TightTolerance);
    }

    [Fact]
    public void TargetHeight_AtToe_ReturnsNativeHeight()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 260, out _);

        Assert.NotNull(profile);
        Assert.Equal(1073.25, profile.TargetHeight(profile.ToeS, 1073.25, profile.ToeHeight), TightTolerance);
        Assert.Equal(1060d, profile.TargetHeight(profile.ToeS, 1060, profile.ToeHeight), TightTolerance);
    }

    [Fact]
    public void TargetHeight_AtBenchStart_ReturnsBenchHeight()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 260, out _);

        Assert.NotNull(profile);
        Assert.Equal(1101.25, profile.TargetHeight(profile.BenchStartS, 1073.25, profile.ToeHeight), TightTolerance);
        Assert.Equal(1101.25, profile.TargetHeight(profile.BenchStartS, 1060, 1090), TightTolerance);
    }

    [Fact]
    public void DesignHeight_EndpointsMatchBenchAndToe()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var south = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 260, out _);
        var north = GeneratedBridgeApproachProfile.TryCreate(740, false, 1101.25, nativeCentreHeight, 10, 260, out _);

        Assert.NotNull(south);
        Assert.NotNull(north);
        Assert.Equal(1101.25, south.DesignHeight(south.BenchStartS), TightTolerance);
        Assert.Equal(south.ToeHeight, south.DesignHeight(south.ToeS), TightTolerance);
        Assert.Equal(1101.25, north.DesignHeight(north.BenchStartS), TightTolerance);
        Assert.Equal(north.ToeHeight, north.DesignHeight(north.ToeS), TightTolerance);
    }

    [Fact]
    public void CrossBlendWeight_EndpointsMatchFlatAndNative()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var south = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 260, out _);
        var north = GeneratedBridgeApproachProfile.TryCreate(740, false, 1101.25, nativeCentreHeight, 10, 260, out _);

        Assert.NotNull(south);
        Assert.NotNull(north);
        Assert.Equal(1d, south.CrossBlendWeight(south.BenchStartS), TightTolerance);
        Assert.Equal(0d, south.CrossBlendWeight(south.ToeS), TightTolerance);
        Assert.Equal(1d, north.CrossBlendWeight(north.BenchStartS), TightTolerance);
        Assert.Equal(0d, north.CrossBlendWeight(north.ToeS), TightTolerance);
    }

    [Fact]
    public void DesignProfile_ClampsBeyondRamp()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var south = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 260, out _);
        var north = GeneratedBridgeApproachProfile.TryCreate(740, false, 1101.25, nativeCentreHeight, 10, 260, out _);

        Assert.NotNull(south);
        Assert.NotNull(north);
        Assert.Equal(0d, south.OutwardDistance(-30), TightTolerance);
        Assert.Equal(260d, south.OutwardDistance(-310), TightTolerance);
        Assert.Equal(1101.25, south.DesignHeight(-30), TightTolerance);
        Assert.Equal(south.ToeHeight, south.DesignHeight(-310), TightTolerance);
        Assert.Equal(1d, south.CrossBlendWeight(-30), TightTolerance);
        Assert.Equal(0d, south.CrossBlendWeight(-310), TightTolerance);
        Assert.Equal(0d, north.OutwardDistance(730), TightTolerance);
        Assert.Equal(260d, north.OutwardDistance(1010), TightTolerance);
        Assert.Equal(1101.25, north.DesignHeight(730), TightTolerance);
        Assert.Equal(north.ToeHeight, north.DesignHeight(1010), TightTolerance);
        Assert.Equal(1d, north.CrossBlendWeight(730), TightTolerance);
        Assert.Equal(0d, north.CrossBlendWeight(1010), TightTolerance);
    }

    [Fact]
    public void TryCreate_FlatTerrain_ReturnsMinimumLength()
    {
        Func<double, double> nativeCentreHeight = _ => 1101.25;
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 260, out var unsupportedReason);

        Assert.NotNull(profile);
        Assert.Null(unsupportedReason);
        Assert.Equal(GeneratedBridgeApproachProfile.MinimumLength, profile.Length, TightTolerance);
        Assert.Equal(0d, profile.DesignMaxGrade, TightTolerance);
        Assert.Equal(1101.25, profile.DesignHeight(profile.BenchStartS), TightTolerance);
        Assert.Equal(1101.25, profile.DesignHeight(profile.ToeS), TightTolerance);
        Assert.Equal(1101.25, profile.DesignHeight((profile.BenchStartS + profile.ToeS) / 2), TightTolerance);
    }

    [Fact]
    public void TryCreate_MaximumLength265_RoundsDownTo260()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 265, out var unsupportedReason);

        Assert.NotNull(profile);
        Assert.Null(unsupportedReason);
        Assert.Equal(260d, profile.Length, TightTolerance);
    }

    [Fact]
    public void TryCreate_MaximumLength259Point9_ReturnsNull()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 259.9, out var unsupportedReason);

        Assert.Null(profile);
        Assert.NotNull(unsupportedReason);
    }

    [Fact]
    public void TryCreate_NoisyTerrain_SmoothsSlope()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300)) + ((int)Math.Round(s / 10) % 2 == 0 ? 0.01953125 : -0.01953125);
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 260, out var unsupportedReason);

        Assert.NotNull(profile);
        Assert.Null(unsupportedReason);
        Assert.Equal(-0.05, profile.ToeSlope, 0.002);
        Assert.Equal(260d, profile.Length, TightTolerance);
    }

    [Fact]
    public void MaximumGradeOf_KnownValues()
    {
        Assert.Equal(0.17604, GeneratedBridgeApproachProfile.MaximumGradeOf(260, -32.5, -0.05), GradeTolerance);
        Assert.Equal(0d, GeneratedBridgeApproachProfile.MaximumGradeOf(100, 0, 0), TightTolerance);
    }

    [Fact]
    public void TryCreate_NullSampler_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, null!, 10, 260, out _));
    }

    [Fact]
    public void TryCreate_NonPositiveLatticeStep_Throws()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));

        Assert.Throws<ArgumentOutOfRangeException>(() => GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 0, 260, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, -10, 260, out _));
    }

    [Fact]
    public void TryCreate_NegativeMaximumLength_Throws()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));

        Assert.Throws<ArgumentOutOfRangeException>(() => GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, -1, out _));
    }

    [Fact]
    public void TryCreate_MaximumLengthBelowMinimum_ReturnsNull()
    {
        Func<double, double> nativeCentreHeight = s => 1068.75 + (0.05 * (s + 300));
        var profile = GeneratedBridgeApproachProfile.TryCreate(-40, true, 1101.25, nativeCentreHeight, 10, 30, out var unsupportedReason);

        Assert.Null(profile);
        Assert.NotNull(unsupportedReason);
    }
}
