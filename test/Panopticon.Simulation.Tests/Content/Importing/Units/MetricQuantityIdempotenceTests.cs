using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

public sealed class MetricQuantityIdempotenceTests
{
    private static readonly UnitConversionPolicy Policy = new(SageGame.CncGenerals);
    private static readonly UnitConversionPolicy MetricGamePolicy = new(SageGame.CncGenerals, gameUnitsPerMeter: 1.0);

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.3048)]
    [InlineData(3.28084)]
    [InlineData(12.5)]
    public void Meters_ToDistanceIsIdentity(double meters)
    {
        Assert.Equal(meters, Policy.ToDistance(Quantity.FromMeters(meters)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.3048)]
    [InlineData(3.28084)]
    [InlineData(12.5)]
    public void Meters_ToGameDistanceAtOneUnitPerMeterIsIdentity(double meters)
    {
        Assert.Equal(meters, MetricGamePolicy.ToGameDistance(Quantity.FromMeters(meters)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.3048)]
    [InlineData(3.28084)]
    public void MetersPerSecond_ToDistancePerFrameDividesByFrameRateOnly(double speed)
    {
        Assert.Equal(speed / Policy.LogicFramesPerSecond, Policy.ToDistancePerFrame(Quantity.FromMetersPerSecond(speed)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.3048)]
    [InlineData(3.28084)]
    public void MetersPerSecondSquared_ToDistancePerFrameSquaredDividesByFrameRateSquaredOnly(double acceleration)
    {
        var frames = Policy.LogicFramesPerSecond;
        Assert.Equal(acceleration / (frames * frames), Policy.ToDistancePerFrameSquared(Quantity.FromMetersPerSecondSquared(acceleration)));
    }
}
