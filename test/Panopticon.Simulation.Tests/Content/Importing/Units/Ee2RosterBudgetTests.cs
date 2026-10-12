using System;
using System.Linq;
using Panopticon.Data;
using Panopticon.Tests.Data;
using Panopticon.Tools.TestHarness;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-M3-03: the committed roster manifest prices through the recorded cross-game rates
/// into two different 1000-unit armies with anti-air whose totals match within the recorded
/// tolerance. Requires CNC_GENERALS_PATH and EMPIRE_EARTH_II_PATH (the manifest prices real
/// Generals BuildCosts and the install's tech-tree resource columns).
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2RosterBudgetTests
{
    [Fact]
    public void BothManifestsPriceToThousandUnitsAndEqualPoints()
    {
        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));

        var report = RosterBudgetEvaluator.Evaluate(RosterBudgetEvaluator.LoadDefault(), game);

        Assert.Equal(2, report.Sides.Count);
        foreach (var side in report.Sides)
        {
            Assert.Equal(1000, side.Units);
            Assert.True(side.AntiAirPresent, $"{side.Id} has no anti-air entry");
            Assert.True(side.HasAircraft, $"{side.Id} has no aircraft entry");
        }

        // Compositions differ: no two sides share the same entry set.
        var definitionsA = report.Sides[0].Entries.Select(e => e.Definition).ToHashSet();
        var definitionsB = report.Sides[1].Entries.Select(e => e.Definition).ToHashSet();
        Assert.False(definitionsA.SetEquals(definitionsB));

        Assert.True(report.WithinTolerance, $"sides differ by {report.PointsDifference:0.##} points ({report.DifferencePercent:0.###}%)");
        Assert.True(report.DifferencePercent <= 0.1, $"measured difference {report.DifferencePercent:0.###}% should sit well inside the authored 0.5%");

        // The authored totals from the manifest comments: 846,375 versus 845,900 (0.056%).
        Assert.Equal(846375, Math.Round(report.Sides[0].Points));
        Assert.Equal(845900, Math.Round(report.Sides[1].Points));
    }

    [Fact]
    public void UnitPricesFollowTheRecordedRates()
    {
        using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals));
        var manifest = RosterBudgetEvaluator.LoadDefault();
        var report = RosterBudgetEvaluator.Evaluate(manifest, game);

        // Generals: BuildCost 1:1 (Crusader 900). EE2: summed resources x 5 (infantry 119
        // resources -> 595; bomber 500 -> 2500).
        var crusader = report.Sides.SelectMany(s => s.Entries).Single(e => e.Definition == "AmericaTankCrusader");
        Assert.Equal(900, crusader.UnitPoints);
        var infantry = report.Sides.SelectMany(s => s.Entries).Single(e => e.Definition == "HeavyInfantry1UpgradeEpoch15");
        Assert.Equal(119, infantry.ResourceSum);
        Assert.Equal(595, infantry.UnitPoints);
        var bomber = report.Sides.SelectMany(s => s.Entries).Single(e => e.Definition == "BomberUpgradeEpoch15");
        Assert.Equal(500, bomber.ResourceSum);
        Assert.Equal(2500, bomber.UnitPoints);
    }
}
