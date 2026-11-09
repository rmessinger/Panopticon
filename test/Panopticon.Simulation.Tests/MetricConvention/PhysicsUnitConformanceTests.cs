using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Physics;
using Xunit;

namespace Panopticon.Tests.MetricConvention;

/// <summary>
/// Pins the Panopticon.Physics copy of the feet-to-meters factor to UnitConversionPolicy, because Physics cannot reference Simulation.
/// </summary>
public sealed class PhysicsUnitConformanceTests
{
    [Fact]
    public void MetersPerLegacyUnit_EqualsUnitConversionPolicyFactor()
    {
        Assert.Equal((float)UnitConversionPolicy.GeneralsFeetToMeters, PhysicsUnitConversion.MetersPerLegacyUnit);
    }
}
