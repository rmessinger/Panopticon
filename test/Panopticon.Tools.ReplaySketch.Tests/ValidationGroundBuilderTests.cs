using System.IO;
using System.Numerics;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class ValidationGroundBuilderTests
{
    private static ValidationGroundScenario SingleFactionScenario()
    {
        // The formation lives in the standard JSON; clone it and restrict to one faction so per-unit
        // assertions are stable. Most tests exercise Rows mode explicitly so the row math stays
        // pinned regardless of what the checked-in standard scenario uses.
        var scenario = ValidationGroundScenarioStore.Standard();
        scenario.FactionIndices = [2];
        scenario.Layout.Mode = LayoutMode.Rows;
        return scenario;
    }

    [Fact]
    public void Build_WithoutOverrides_DerivesPositionsFromLayout()
    {
        // Row math pinned: the standard centre (600,600) with 3 factions.
        var scenario = ValidationGroundScenarioStore.Standard();
        scenario.Layout.Mode = LayoutMode.Rows;
        var plan = ValidationGroundBuilder.Build(scenario);

        // Faction 0 (USA): unit row Y = centre.Y - totalSpan/2 = 600 - 270 = 330 (totalSpan =
        // 3*TargetApproach + 2*FactionRowOffset = 240 + 300). Row origin X centres the 10-unit
        // roster on centre.X: 600 - 9*80/2 = 240. Unit 0 is at the row origin; its target is on the
        // target row (unitRowY + TargetApproach = 410) at the same X.
        Assert.Equal(new Vector3(240f, 330f, 0f), plan.UnitPlacements[0].Position);
        Assert.Equal(new Vector3(240f, 410f, 0f), plan.TargetPlacements[0].Position);
    }

    [Fact]
    public void Build_ClustersOnFlatMap_PlacesBlocksAtLayoutStandoff()
    {
        // Cluster mode without a map: terrain term is uniform, so the first cluster lands on the
        // grid point nearest the layout centre. Relational assertions only — exact coordinates
        // depend on the search grid.
        var scenario = SingleFactionScenario();
        scenario.Layout.Mode = LayoutMode.Clusters;
        var plan = ValidationGroundBuilder.Build(scenario, MapMetadataService.CreateForTesting(
            new Vector3(0f, 0f, 0f), new Vector3(2390f, 2590f, 0f)));

        Assert.Equal(plan.UnitPlacements.Count, plan.TargetPlacements.Count);
        foreach (var unit in plan.UnitPlacements)
        {
            var target = plan.TargetPlacements[unit.TargetIndex];
            var distance = Vector2.Distance(
                new Vector2(unit.Position.X, unit.Position.Y),
                new Vector2(target.Position.X, target.Position.Y));
            Assert.Equal(scenario.Layout.TargetApproachDistance, distance, precision: 0);
        }
    }

    [Fact]
    public void Build_Clusters_TargetsClearOfUnitsAndOtherTargets()
    {
        // Regression for the map-view overlap (target square drawn on a unit circle): a purely
        // radial placement lands interior units' targets exactly on their neighbours. The builder
        // must rotate each target to clear every block unit and every other target.
        var scenario = ValidationGroundScenarioStore.Standard();
        scenario.Layout.Mode = LayoutMode.Clusters;
        var plan = ValidationGroundBuilder.Build(scenario, MapMetadataService.CreateForTesting(
            new Vector3(0f, 0f, 0f), new Vector3(2390f, 2590f, 0f)));

        var unitPositions = plan.UnitPlacements
            .Select(p => new Vector2(p.Position.X, p.Position.Y)).ToList();
        var targetPositions = plan.TargetPlacements
            .Select(p => new Vector2(p.Position.X, p.Position.Y)).ToList();

        // Each target is checked against every unit except its own (the terrorist's own target is
        // deliberately 8 units away) and against every other target.
        for (var i = 0; i < targetPositions.Count; i++)
        {
            var ownUnit = unitPositions[i];
            foreach (var unit in unitPositions)
            {
                if (unit == ownUnit) continue;
                Assert.True(Vector2.Distance(targetPositions[i], unit) >= 30f,
                    $"target {targetPositions[i]} overlaps a unit ({Vector2.Distance(targetPositions[i], unit):F0} away)");
            }

            for (var j = 0; j < targetPositions.Count; j++)
            {
                if (i == j) continue;
                Assert.True(Vector2.Distance(targetPositions[i], targetPositions[j]) >= 30f,
                    $"target {targetPositions[i]} overlaps another target");
            }
        }
    }

    [Fact]
    public void Build_ClustersOnFlatMap_FactionsDoNotOverlap()
    {
        var scenario = ValidationGroundScenarioStore.Standard();
        scenario.Layout.Mode = LayoutMode.Clusters;
        var plan = ValidationGroundBuilder.Build(scenario, MapMetadataService.CreateForTesting(
            new Vector3(0f, 0f, 0f), new Vector3(2390f, 2590f, 0f)));

        // Cluster centres (faction unit centroids) must respect the separation knob.
        foreach (var factionA in scenario.FactionIndices)
        {
            foreach (var factionB in scenario.FactionIndices)
            {
                if (factionA >= factionB) continue;
                var centroidA = Centroid(plan, factionA);
                var centroidB = Centroid(plan, factionB);
                Assert.True(Vector2.Distance(centroidA, centroidB) >= scenario.Layout.FactionRowOffset,
                    $"clusters {factionA}/{factionB} overlap: {Vector2.Distance(centroidA, centroidB)}");
            }
        }
    }

    private static Vector2 Centroid(SpawnPlan plan, int factionIndex)
    {
        var placements = plan.UnitPlacements.Where(p => p.FactionIndex == factionIndex).ToList();
        return new Vector2(
            placements.Average(p => p.Position.X),
            placements.Average(p => p.Position.Y));
    }

    [Fact]
    public void Build_Clusters_TargetDistanceOverrideReplacesStandoff()
    {
        // The terrorist's proximity pin: unit-side TargetDistance overrides the standoff without
        // pinning any absolute position (cluster placement stays terrain-derived).
        var scenario = SingleFactionScenario();
        scenario.Layout.Mode = LayoutMode.Clusters;
        scenario.PlacementOverrides.Add(new PlacementOverride
        {
            FactionIndex = 2,
            RosterIndex = 0,
            IsTarget = false,
            TargetDistance = 8f,
        });

        var plan = ValidationGroundBuilder.Build(scenario, MapMetadataService.CreateForTesting(
            new Vector3(0f, 0f, 0f), new Vector3(2390f, 2590f, 0f)));

        var unit = plan.UnitPlacements[0];
        var target = plan.TargetPlacements[unit.TargetIndex];
        var distance = Vector2.Distance(
            new Vector2(unit.Position.X, unit.Position.Y),
            new Vector2(target.Position.X, target.Position.Y));
        Assert.Equal(8f, distance, precision: 0);
    }

    [Fact]
    public void Build_WithUnitOverride_UsesOverridePosition()
    {
        var scenario = SingleFactionScenario();
        scenario.PlacementOverrides.Add(new PlacementOverride
        {
            FactionIndex = 2,
            RosterIndex = 0,
            IsTarget = false,
            X = 500f,
            Y = 600f,
        });

        var plan = ValidationGroundBuilder.Build(scenario);

        Assert.Equal(new Vector3(500f, 600f, 0f), plan.UnitPlacements[0].Position);
    }

    [Fact]
    public void Build_UnitOverride_TargetTracksUnitXOnTargetRow()
    {
        // The target sits on the faction's target row at the unit's X; moving a unit keeps its
        // target on that row, tracking the unit's X (here 500), unless a target override pins it.
        var scenario = SingleFactionScenario();
        scenario.PlacementOverrides.Add(new PlacementOverride
        {
            FactionIndex = 2,
            RosterIndex = 0,
            IsTarget = false,
            X = 500f,
            Y = 1000f,
        });

        var plan = ValidationGroundBuilder.Build(scenario);

        // Single faction: unit row Y = 600 - 80/2 = 560, target row Y = 560 + 80 = 640.
        Assert.Equal(new Vector3(500f, 640f, 0f), plan.TargetPlacements[0].Position);
    }

    [Fact]
    public void Build_WithTargetOverride_UsesOverridePosition()
    {
        var scenario = SingleFactionScenario();
        scenario.PlacementOverrides.Add(new PlacementOverride
        {
            FactionIndex = 2,
            RosterIndex = 0,
            IsTarget = true,
            X = 700f,
            Y = 800f,
        });

        var plan = ValidationGroundBuilder.Build(scenario);

        Assert.Equal(new Vector3(700f, 800f, 0f), plan.TargetPlacements[0].Position);
    }

    [Fact]
    public void Build_WithStaleOverride_IsIgnored()
    {
        // An override for a roster slot that does not exist must not throw; generated placements
        // are unaffected.
        var scenario = SingleFactionScenario();
        scenario.PlacementOverrides.Add(new PlacementOverride
        {
            FactionIndex = 2,
            RosterIndex = 99,
            IsTarget = false,
            X = 1f,
            Y = 2f,
        });

        var plan = ValidationGroundBuilder.Build(scenario);

        Assert.NotEmpty(plan.UnitPlacements);
        Assert.True(plan.UnitPlacements[0].Position.X != 1f);
    }

    [Fact]
    public void Build_SingleFaction_UnitTargetPairIsCentredOnCentre()
    {
        // With one faction the formation is one unit row + one target row; the pair is centred on
        // the centre Y (unit row below, target row above), so neither sits exactly on centre.Y.
        var scenario = SingleFactionScenario();

        var plan = ValidationGroundBuilder.Build(scenario);

        var unitY = plan.UnitPlacements[0].Position.Y;
        var targetY = plan.TargetPlacements[0].Position.Y;
        Assert.Equal(560f, unitY);
        Assert.Equal(640f, targetY);
        Assert.Equal(600f, (unitY + targetY) / 2f);
    }

    [Fact]
    public void SaveLoad_RoundTripsLayoutAndOverrides()
    {
        var scenario = new ValidationGroundScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            FactionIndices = [2],
            TargetDefinitions = ["AmericaBarracks"],
            Layout =
            {
                TargetClusterCenter = new Vector3(1234f, 5678f, 0f),
                UnitSpacing = 42f,
            },
        };
        scenario.PlacementOverrides.Add(new PlacementOverride
        {
            FactionIndex = 2,
            RosterIndex = 0,
            IsTarget = true,
            X = 111f,
            Y = 222f,
        });
        var path = Path.Combine(Path.GetTempPath(), $"vg_scenario_{System.Guid.NewGuid():N}.json");

        try
        {
            ValidationGroundScenarioStore.Save(scenario, path);
            var loaded = ValidationGroundScenarioStore.Load(path);

            Assert.Equal(new Vector3(1234f, 5678f, 0f), loaded.Layout.TargetClusterCenter);
            Assert.Equal(42f, loaded.Layout.UnitSpacing);
            Assert.Single(loaded.PlacementOverrides);
            Assert.Equal(111f, loaded.PlacementOverrides[0].X);
            Assert.True(loaded.PlacementOverrides[0].IsTarget);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Load_NonexistentFile_ReturnsStandard()
    {
        // A missing scenario file falls back to the standard embedded configuration, not an empty
        // scenario, so a typo never silently produces a degenerate (zero-layout) run.
        var loaded = ValidationGroundScenarioStore.Load(Path.Combine(Path.GetTempPath(), "does_not_exist.json"));

        Assert.Equal(ValidationGroundScenarioStore.Standard().Layout.TargetClusterCenter, loaded.Layout.TargetClusterCenter);
        Assert.NotEmpty(loaded.FactionIndices);
        Assert.True(loaded.Layout.UnitSpacing > 0f);
    }

    [Fact]
    public void Standard_LoadsCheckedInFormation()
    {
        // The formation is configured by the checked-in JSON, not source. Pin a few values so a
        // silent change to the file (or an accidental re-introduction of source defaults) is caught.
        var standard = ValidationGroundScenarioStore.Standard();

        Assert.Equal(new Vector3(600f, 600f, 0f), standard.Layout.TargetClusterCenter);
        Assert.Equal(80f, standard.Layout.UnitSpacing);
        Assert.Equal(150f, standard.Layout.FactionRowOffset);
        Assert.Equal(80f, standard.Layout.TargetApproachDistance);
        Assert.Equal([2, 3, 4], standard.FactionIndices);
        Assert.Equal(["CarAsian1"], standard.TargetDefinitions);
        // The terrorist's close-quarters override (WI-VG-04): its suicide weapon has range 1, so
        // the unit is hand-placed next to its target instead of the 80-unit standoff.
        var terrorist = Assert.Single(standard.PlacementOverrides);
        Assert.Equal(4, terrorist.FactionIndex);
        Assert.Equal(3, terrorist.RosterIndex);
        Assert.False(terrorist.IsTarget);
    }

    [Fact]
    public void Build_VanillaGame_SkipsZeroHourOnlyUnits()
    {
        // WI-VG-03: Zero Hour-only roster entries are skipped on a vanilla Generals run (no unit,
        // no target, no resolution gap) and included when the scenario targets Zero Hour. The USA
        // roster has no Zero Hour-only entries, so assert on the China roster.
        var vanilla = SingleFactionScenario();
        vanilla.Game = "CncGenerals";
        vanilla.FactionIndices = [3];

        var zh = SingleFactionScenario();
        zh.Game = "CncGeneralsZeroHour";
        zh.FactionIndices = [3];

        var vanillaPlan = ValidationGroundBuilder.Build(vanilla);
        var zhPlan = ValidationGroundBuilder.Build(zh);

        Assert.DoesNotContain(vanillaPlan.UnitPlacements, p => p.DefinitionName == "ChinaVehicleHelix");
        Assert.Equal(vanillaPlan.UnitPlacements.Count, vanillaPlan.TargetPlacements.Count);

        Assert.Contains(zhPlan.UnitPlacements, p => p.DefinitionName == "ChinaVehicleHelix");
        Assert.Equal(zhPlan.UnitPlacements.Count, zhPlan.TargetPlacements.Count);
    }

    [Fact]
    public void LoadThenBuild_AppliesOverridenPositionToEndToEnd()
    {
        // Mirrors the TestHarness path (BuildValidationGroundRun): Load a JSON scenario, then Build,
        // and confirm a hand-edited override reaches the generated SpawnPlan. Regression for the
        // Phase 2 wiring that feeds ReplaySketch-edited layouts into a run.
        var source = SingleFactionScenario();
        source.PlacementOverrides.Add(new PlacementOverride
        {
            FactionIndex = 2,
            RosterIndex = 0,
            IsTarget = false,
            X = 1234f,
            Y = 567f,
        });
        var path = Path.Combine(Path.GetTempPath(), $"vg_loadbuild_{System.Guid.NewGuid():N}.json");

        try
        {
            ValidationGroundScenarioStore.Save(source, path);
            var loaded = ValidationGroundScenarioStore.Load(path);
            var plan = ValidationGroundBuilder.Build(loaded);

            Assert.Equal(new Vector3(1234f, 567f, 0f), plan.UnitPlacements[0].Position);
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
