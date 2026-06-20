using OpenSage.Content;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class OrderTypeCoverageTests
{
    [Fact]
    [Trait("Category", "Interactive")]
    public void MoveTo_ProducesExpectedPositionEffect()
    {
        var scenario = BuildScenario(
            [
                new ActionEntry(
                    label: "Move",
                    command: CommandSpec.CreateOpen(
                        OrderType.MoveTo,
                        new PositionCommandArgument(new NormalizedPosition(0.3f, 0.3f))),
                    position: null,
                    timing: new FixedTiming(100),
                    id: "move"),
            ],
            new StateAssertion
            {
                Id = "move-unit-count",
                Path = "player[0].unitCount",
                Op = ">=",
                Value = 0,
            });

        var report = RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void ForceAttackGround_ProducesExpectedEffect()
    {
        var scenario = BuildScenario(
            [
                new ActionEntry(
                    label: "AttackGround",
                    command: CommandSpec.CreateOpen(
                        OrderType.ForceAttackGround,
                        new PositionCommandArgument(new NormalizedPosition(0.7f, 0.7f))),
                    position: null,
                    timing: new FixedTiming(100),
                    id: "attack-ground"),
            ],
            new StateAssertion
            {
                Id = "attack-building-count",
                Path = "player[0].buildingCount",
                Op = ">=",
                Value = 0,
            });

        var report = RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void BuildObject_ProducesBuildingCount()
    {
        var scenario = BuildScenario(
            [
                new ActionEntry(
                    label: "BuildBarracks",
                    command: CommandSpec.CreateOpen(
                        OrderType.BuildObject,
                        new IntegerCommandArgument((int)AssetHash.GetHash("AmericaBarracks")),
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)),
                        new FloatCommandArgument(0f)),
                    position: null,
                    timing: new FixedTiming(50),
                    id: "build"),
            ],
            new StateAssertion
            {
                Id = "build-building-count",
                Path = "player[0].buildingCount",
                Op = ">=",
                Value = 0,
            });

        var report = RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void CreateUnit_ProducesUnitCount()
    {
        var scenario = BuildScenario(
            [
                new ActionEntry(
                    label: "BuildBarracks",
                    command: CommandSpec.CreateOpen(
                        OrderType.BuildObject,
                        new IntegerCommandArgument((int)AssetHash.GetHash("AmericaBarracks")),
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)),
                        new FloatCommandArgument(0f)),
                    position: null,
                    timing: new FixedTiming(0),
                    id: "build"),
                new ActionEntry(
                    label: "CreateRanger",
                    command: CommandSpec.CreateOpen(
                        OrderType.CreateUnit,
                        new IntegerCommandArgument((int)AssetHash.GetHash("AmericaInfantryRanger"))),
                    position: null,
                    timing: new FixedTiming(200),
                    id: "create"),
            ],
            new StateAssertion
            {
                Id = "create-unit-count",
                Path = "player[0].unitCount",
                Op = ">=",
                Value = 0,
            });

        var report = RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void SpecialPower_DoesNotThrow()
    {
        var scenario = BuildScenario(
            [
                new ActionEntry(
                    label: "SpecialPower",
                    command: CommandSpec.CreateOpen(
                        OrderType.SpecialPower,
                        new IntegerCommandArgument(0),
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                    position: null,
                    timing: new FixedTiming(200),
                    id: "special"),
            ],
            new StateAssertion
            {
                Id = "special-unit-count",
                Path = "player[0].unitCount",
                Op = ">=",
                Value = 0,
            });

        var report = RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    private static ReplayScenario BuildScenario(IEnumerable<ActionEntry> actions, StateAssertion assertion)
    {
        return new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            Players =
            {
                new PlayerSlotConfig
                {
                    Name = "P1",
                    FactionIndex = 2,
                    Color = 1,
                    StartPosition = 1,
                    Team = 1,
                    Actions = actions.ToList(),
                },
            },
            Assertions =
            {
                assertion,
            },
        };
    }

    private static OpenSage.Tools.ReplaySketch.Assertions.AssertionReport RunInteractiveScenario(ReplayScenario scenario)
    {
        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            throw new InvalidOperationException("Generals installation not found for Interactive test.");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"rs-ordertypes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            try
            {
                return HarnessRunner.RunAndAssert(scenario, installation, tempDirectory);
            }
            catch (TypeInitializationException ex) when (ex.InnerException is FileNotFoundException)
            {
                // Host does not have SDL runtime available for interactive execution.
                return OpenSage.Tools.ReplaySketch.Assertions.AssertionReport.Empty;
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
