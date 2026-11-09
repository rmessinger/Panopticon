#nullable enable

using System;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

public sealed class UnitConversionPolicyTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void DimensionalPowers_LengthAreaVolumeInverseLength_ScaleCorrectly()
    {
        const double s = UnitConversionPolicy.GeneralsFeetToMeters;
        Assert.Equal(0.3048, s);

        // Length (L^1): scales by s
        const double legacyLength = 10.0;
        var scaledLength = UnitConversionPolicy.ScaleLength(legacyLength);
        Assert.Equal(3.048, scaledLength, precision: 6);
        Assert.Equal(legacyLength, UnitConversionPolicy.UnscaleLength(scaledLength), precision: 6);

        // Area (L^2): scales by s^2
        const double legacyArea = 100.0;
        var scaledArea = UnitConversionPolicy.ScaleArea(legacyArea);
        Assert.Equal(legacyArea * s * s, scaledArea, precision: 6);
        Assert.Equal(legacyArea, UnitConversionPolicy.UnscaleArea(scaledArea), precision: 6);

        // Volume (L^3): scales by s^3
        const double legacyVolume = 1000.0;
        var scaledVolume = UnitConversionPolicy.ScaleVolume(legacyVolume);
        Assert.Equal(legacyVolume * s * s * s, scaledVolume, precision: 6);
        Assert.Equal(legacyVolume, UnitConversionPolicy.UnscaleVolume(scaledVolume), precision: 6);

        // Inverse-length (L^-1): scales by 1/s
        const double legacyInverse = 2.0;
        var scaledInverse = UnitConversionPolicy.ScaleInverseLength(legacyInverse);
        Assert.Equal(legacyInverse / s, scaledInverse, precision: 6);
        Assert.Equal(legacyInverse, UnitConversionPolicy.UnscaleInverseLength(scaledInverse), precision: 6);
    }

    [Fact]
    public void NonLengthQuantities_RemainUnchanged()
    {
        Assert.Equal(45.0, UnitConversionPolicy.ScaleAngle(45.0));
        Assert.Equal(2.5, UnitConversionPolicy.ScaleTime(2.5));
        Assert.Equal(150.0, UnitConversionPolicy.ScaleHealth(150.0));
        Assert.Equal(75.0, UnitConversionPolicy.ScaleDamage(75.0));
        Assert.Equal(0.75, UnitConversionPolicy.ScaleRatio(0.75));
    }

    [Fact]
    public void Ee2PredeterminedExpectations_MatchDoc()
    {
        // Predetermined expectations from docs/roadmap/deep-dives/h2b-metric-core.md:
        // 12 * 0.3048 = 3.6576 for profile 12
        var profile12Distance = UnitConversionPolicy.ScaleEe2SourceDistance(12.0);
        Assert.Equal(3.6576, profile12Distance, precision: 6);

        // 18.2505 * 0.3048 = 5.5627524 for 18.2505
        var refDistance = UnitConversionPolicy.ScaleEe2SourceDistance(18.2505);
        Assert.Equal(5.5627524, refDistance, precision: 6);

        // 0.36501 * 0.3048 = 0.111255048 for 0.36501
        var refModelUnit = UnitConversionPolicy.ScaleEe2SourceModelUnit(0.36501);
        Assert.Equal(0.111255048, refModelUnit, precision: 6);
    }

    [Theory]
    [InlineData(SageGame.CncGenerals, true, false)]
    [InlineData(SageGame.CncGeneralsZeroHour, true, false)]
    [InlineData(SageGame.Bfme, false, true)]
    [InlineData(SageGame.Bfme2, false, true)]
    [InlineData(SageGame.Bfme2Rotwk, false, true)]
    public void SourceUnitPolicy_PolicyExplicitness_SupportedGamesDeclared(
        SageGame game,
        bool expectedEstablished,
        bool expectedProvisional)
    {
        var policy = SourceUnitPolicy.ForGame(game);
        Assert.Equal(game, policy.Game);
        Assert.Equal(0.3048, policy.MetersPerSourceUnit);
        Assert.Equal(expectedEstablished, policy.IsEstablishedPhysical);
        Assert.Equal(expectedProvisional, policy.IsProvisional);

        if (expectedEstablished)
        {
            Assert.Equal(SourceUnitStatus.EstablishedPhysical, policy.Status);
            Assert.Contains("Established physical", policy.Description, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(SourceUnitStatus.ProvisionalCompatibility, policy.Status);
            Assert.Contains("Provisional compatibility", policy.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(SageGame.Cnc3)]
    [InlineData(SageGame.Cnc3KanesWrath)]
    [InlineData(SageGame.Ra3)]
    [InlineData(SageGame.Ra3Uprising)]
    [InlineData(SageGame.Cnc4)]
    public void SourceUnitPolicy_UnsupportedGames_ThrowNotSupported_NoDefaultFallthrough(SageGame game)
    {
        Assert.Throws<NotSupportedException>(() => SourceUnitPolicy.ForGame(game));
    }

    [Fact]
    public void SourceUnitPolicy_InvalidGame_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceUnitPolicy.ForGame((SageGame)9999));
    }

    [Fact]
    public void SourceUnitPolicy_OverridePoint_ReplacesPolicyUntilCleared()
    {
        try
        {
            var customPolicy = new SourceUnitPolicy(
                SageGame.Bfme,
                0.5,
                SourceUnitStatus.EstablishedPhysical,
                "Custom physical calibration.");

            SourceUnitPolicy.SetOverride(SageGame.Bfme, customPolicy);
            var retrieved = SourceUnitPolicy.ForGame(SageGame.Bfme);

            Assert.Equal(0.5, retrieved.MetersPerSourceUnit);
            Assert.True(retrieved.IsEstablishedPhysical);
            Assert.Equal("Custom physical calibration.", retrieved.Description);

            SourceUnitPolicy.SetOverride(SageGame.Bfme, null);
            var restored = SourceUnitPolicy.ForGame(SageGame.Bfme);

            Assert.Equal(0.3048, restored.MetersPerSourceUnit);
            Assert.True(restored.IsProvisional);
        }
        finally
        {
            SourceUnitPolicy.ClearOverrides();
        }
    }

    [Fact]
    public void NoDoubleScale_MetricSpeedAndAcceleration_AreNotMultipliedByScaleFactor()
    {
        var policy = new UnitConversionPolicy(SageGame.CncGenerals);
        Assert.Equal(30, policy.LogicFramesPerSecond);

        // 30 meters per second at 30 logic frames per second = 1.0 meter per logic frame.
        // If double-scaled, it would incorrectly multiply by 0.3048 to yield 0.3048.
        var metricSpeed = Quantity.FromMetersPerSecond(30.0);
        var distancePerFrame = policy.ToDistancePerFrame(metricSpeed);
        Assert.Equal(1.0, distancePerFrame);

        // 90 meters per second squared at 30 fps = 90 / 900 = 0.1 meters per frame squared.
        // If double-scaled, it would incorrectly multiply by 0.3048.
        var metricAccel = Quantity.FromMetersPerSecondSquared(90.0);
        var accelPerFrame = policy.ToDistancePerFrameSquared(metricAccel);
        Assert.Equal(0.1, accelPerFrame);

        // Idempotent metric distance:
        var metricDist = Quantity.FromMeters(12.5);
        Assert.Equal(12.5, policy.ToDistance(metricDist));
    }

    [Fact]
    public void QuantityTags_GameDistanceAndMetricTags_AreDistinctAndInspected()
    {
        var m = Quantity.FromMeters(5.0);
        Assert.Equal(QuantityUnit.Meters, m.Unit);
        Assert.True(m.IsMetricDistance);
        Assert.True(m.IsMetric);

        var mps = Quantity.FromMetersPerSecond(15.0);
        Assert.Equal(QuantityUnit.MetersPerSecond, mps.Unit);
        Assert.True(mps.IsMetricSpeed);
        Assert.True(mps.IsMetric);

        var mps2 = Quantity.FromMetersPerSecondSquared(3.0);
        Assert.Equal(QuantityUnit.MetersPerSecondSquared, mps2.Unit);
        Assert.True(mps2.IsMetricAcceleration);
        Assert.True(mps2.IsMetric);

        var legacyDistance = Quantity.FromGameDistance(10.0);
        Assert.Equal(QuantityUnit.GameDistance, legacyDistance.Unit);
        Assert.False(legacyDistance.IsMetric);
        Assert.False(legacyDistance.IsMetricDistance);

        var legacySpeed = new Quantity(30.0, QuantityUnit.DistancePerSecond);
        Assert.False(legacySpeed.IsMetric);
    }
}
