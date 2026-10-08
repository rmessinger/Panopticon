using System.Linq;
using Xunit;

namespace Panopticon.Tests.Graphics.Proportions;

/// <summary>
/// Pure arithmetic re-derivation of the G10 proportions audit claims from the committed
/// reference manifest. No installation and no GPU: the manifest constants are the audited
/// measurements of WI-G10-01.
/// </summary>
public class ReferenceScaleArithmeticTests
{
    private static readonly ReferenceScaleManifest Manifest = ReferenceScaleManifest.Load();

    private static ReferenceScaleObject Get(string objectName)
    {
        return Manifest.Objects.Single(o => o.ObjectName == objectName);
    }

    [Fact]
    public void TestBaselineUnitsPerMeterMatchesHullAnchor()
    {
        var baseline = Manifest.Baseline;

        Assert.Equal(
            baseline.HullLengthUnits / baseline.ReferenceMeters,
            baseline.UnitsPerMeter,
            precision: 3);
    }

    [Fact]
    public void TestInfantryIsOversizedRelativeToTankBaseline()
    {
        var upm = Manifest.Baseline.UnitsPerMeter;
        var ranger = Get("AmericaInfantryRanger");

        var renderedHeight = ranger.Extents.Z / upm;
        Assert.InRange((double)renderedHeight, 3.8, 4.2);

        var oversize = renderedHeight / ranger.ReferenceMeters!.Value;
        Assert.InRange((double)oversize, 2.1, 2.5);
    }

    [Fact]
    public void TestJetNeedsUpscaleInClaimedBand()
    {
        var upm = Manifest.Baseline.UnitsPerMeter;
        var mig = Get("ChinaJetMiG");

        var renderedLength = mig.Extents.X / upm;
        Assert.InRange((double)renderedLength, 8.8, 9.3);

        var neededMultiplier = mig.ReferenceMeters!.Value / renderedLength;
        Assert.InRange((double)neededMultiplier, 1.8, 2.1);

        // The community "~5 units per metre" baseline lands at the upper end of the same claim.
        var claimedUnitsPerMeter = 5.0m;
        var multiplierAtClaimedBaseline = mig.ReferenceMeters.Value * claimedUnitsPerMeter / mig.Extents.X;
        Assert.InRange((double)multiplierAtClaimedBaseline, 2.4, 2.8);
    }

    [Fact]
    public void TestHelicopterNeedsLessUpscaleThanJet()
    {
        var upm = Manifest.Baseline.UnitsPerMeter;
        var comanche = Get("AmericaVehicleComanche");
        var mig = Get("ChinaJetMiG");

        var comancheMultiplier = comanche.ReferenceMeters!.Value / (comanche.Extents.X / upm);
        Assert.InRange((double)comancheMultiplier, 1.05, 1.35);

        var migMultiplier = mig.ReferenceMeters!.Value / (mig.Extents.X / upm);
        Assert.True(comancheMultiplier < migMultiplier, "the jet hull must be proportionally smaller than the helicopter fuselage");
    }

    [Fact]
    public void TestClassesDoNotShareOneUnitsPerMeter()
    {
        var ranger = Get("AmericaInfantryRanger");
        var mig = Get("ChinaJetMiG");

        var infantryUnitsPerMeter = ranger.Extents.Z / ranger.ReferenceMeters!.Value;
        var jetUnitsPerMeter = mig.Extents.X / mig.ReferenceMeters!.Value;

        Assert.InRange((double)infantryUnitsPerMeter, 8.0, 8.7);
        Assert.InRange((double)jetUnitsPerMeter, 1.8, 2.0);
        Assert.True(infantryUnitsPerMeter / jetUnitsPerMeter > 2.5m, "one global scale constant cannot serve both classes");
    }

    [Fact]
    public void TestBuildingFootprintStaysPlausible()
    {
        var plant = Get("AmericaPowerPlant");

        // Consistency datapoint only: the plant has no strong real-world analog.
        var footprintMeters = plant.Extents.X / Manifest.Baseline.UnitsPerMeter;
        Assert.InRange((double)footprintMeters, 11.5, 13.5);
    }
}
