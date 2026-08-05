using System.IO;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ValidationGroundBuilderTests
{
    private static ValidationGroundScenario SingleFactionScenario() => new()
    {
        FactionIndices = [2],
        TargetDefinitions = ["AmericaBarracks"],
    };

    [Fact]
    public void Build_WithoutOverrides_DerivesPositionsFromLayout()
    {
        var scenario = new ValidationGroundScenario
        {
            FactionIndices = [2, 3, 4],
            TargetDefinitions = ["AmericaBarracks"],
        };

        var plan = ValidationGroundBuilder.Build(scenario);

        // Row 0 (USA, faction 2): origin (750, 850) with the default centre (1000,1000) and
        // FactionRowOffset 150; unit 0 sits at the row origin.
        Assert.Equal(new Vector3(750f, 850f, 0f), plan.UnitPlacements[0].Position);
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
    public void Build_UnitOverride_RepositionsLinkedTargetInFrontUnlessAlsoOverridden()
    {
        // The target derives from the (possibly overridden) unit position, so moving a unit keeps
        // its target TargetApproachDistance in front of it toward the cluster centre.
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

        // centre (1000,1000): direction from (500,1000) is +X; target at 500 + 90 = 590.
        Assert.Equal(new Vector3(590f, 1000f, 0f), plan.TargetPlacements[0].Position);
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
    public void Build_SingleFactionRow_IsCentredOnClusterY()
    {
        // Regression for the hard-coded rowCount=3 bug: with one faction the row block height is
        // zero, so the lone row sits on the cluster Y rather than being offset by the old 3-row
        // centring math.
        var scenario = SingleFactionScenario();

        var plan = ValidationGroundBuilder.Build(scenario);

        Assert.Equal(1000f, plan.UnitPlacements[0].Position.Y);
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
            scenario.Save(path);
            var loaded = ValidationGroundScenario.Load(path);

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
    public void Load_NonexistentFile_ReturnsDefault()
    {
        var loaded = ValidationGroundScenario.Load(Path.Combine(Path.GetTempPath(), "does_not_exist.json"));

        Assert.Empty(loaded.PlacementOverrides);
        Assert.NotEmpty(loaded.FactionIndices);
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
            source.Save(path);
            var loaded = ValidationGroundScenario.Load(path);
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
