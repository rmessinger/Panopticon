using System.IO;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class UnitTimingBaselineTests
{
    private static UnitTiming NewTiming(string name, int faction, uint frames) =>
        new(name, faction, PrimaryActionKind.Attack, frames);

    [Fact, Trait("Category", "Roadmap")]
    public void ValueWithinPercentageBand_Passes()
    {
        var baseline = new UnitTimingBaseline
        {
            Entries =
            {
                new UnitTimingBaselineEntry
                {
                    DefinitionName = "Unit", FactionIndex = 2,
                    GoldenFrames = 100, TolerancePercent = 10, MinimumFrameFloor = 2,
                },
            },
        };

        var report = new ValidationGroundReport();
        report.Timings.Add(NewTiming("Unit", 2, 108)); // 8% over, within 10% band

        Assert.False(baseline.HasRegression(report));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void ValueOutsidePercentageBand_Regresses()
    {
        var baseline = new UnitTimingBaseline
        {
            Entries =
            {
                new UnitTimingBaselineEntry
                {
                    DefinitionName = "Unit", FactionIndex = 2,
                    GoldenFrames = 100, TolerancePercent = 10, MinimumFrameFloor = 2,
                },
            },
        };

        var report = new ValidationGroundReport();
        report.Timings.Add(NewTiming("Unit", 2, 130)); // 30% over, outside 10% band

        Assert.True(baseline.HasRegression(report));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void MinimumFrameFloor_PreventsOneFrameJitterFailingShortAction()
    {
        var baseline = new UnitTimingBaseline
        {
            Entries =
            {
                new UnitTimingBaselineEntry
                {
                    DefinitionName = "Short", FactionIndex = 2,
                    GoldenFrames = 20, TolerancePercent = 1, MinimumFrameFloor = 5,
                },
            },
        };

        var report = new ValidationGroundReport();
        // 20 * 1% = 0.2 band, but the floor of 5 frames allows a 1-frame jitter to pass.
        report.Timings.Add(NewTiming("Short", 2, 21));

        Assert.False(baseline.HasRegression(report));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void UnitMissingFromBaseline_IsReportedAsNew_NotFailure()
    {
        var baseline = new UnitTimingBaseline();
        var report = new ValidationGroundReport();
        report.Timings.Add(NewTiming("BrandNew", 3, 50));

        Assert.False(baseline.HasRegression(report));
        Assert.Equal(BaselineEntryVerdict.New, baseline.Compare(report)[0].Verdict);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SaveAndLoad_RoundTripsEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"p9baseline_{System.Guid.NewGuid():N}.json");
        try
        {
            var baseline = new UnitTimingBaseline
            {
                Entries =
                {
                    new UnitTimingBaselineEntry
                    {
                        DefinitionName = "Unit", FactionIndex = 2, ActionKind = "Attack",
                        GoldenFrames = 42, TolerancePercent = 5, MinimumFrameFloor = 3,
                    },
                },
            };
            baseline.Save(path);

            var loaded = UnitTimingBaseline.Load(path);
            var entry = Assert.Single(loaded.Entries);
            Assert.Equal(42u, entry.GoldenFrames);
            Assert.Equal("Attack", entry.ActionKind);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
