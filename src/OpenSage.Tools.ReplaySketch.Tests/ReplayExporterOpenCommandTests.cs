using System.Numerics;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ReplayExporterOpenCommandTests
{
    [Fact]
    public void OpenCommand_WithFixedTiming_ExportsSuccessfully()
    {
        var scenario = CreateScenario(
            new ActionEntry(
                label: "Move",
                command: CommandSpec.CreateOpen(
                    OrderType.MoveTo,
                    new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                position: null,
                timing: new FixedTiming(100),
                id: "cmd-move"));

        var map = MapMetadataService.CreateForTesting(
            extentMin: Vector3.Zero,
            extentMax: new Vector3(100f, 100f, 20f),
            playerStarts: Vector3.Zero);

        var outputPath = Path.Combine(Path.GetTempPath(), $"rs-open-{Guid.NewGuid():N}.rep");
        try
        {
            var error = ReplayExporter.Export(scenario, map, outputPath, out var deferredActions);

            Assert.Null(error);
            Assert.Empty(deferredActions);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void OpenCommand_AfterActionTrigger_ThrowsInvalidOperation()
    {
        var triggerAction = new ActionEntry(
            label: "A",
            command: CommandSpec.CreateOpen(
                OrderType.MoveTo,
                new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
            position: null,
            timing: new FixedTiming(100),
            id: "cmd-a");

        var dependentAction = new ActionEntry(
            label: "B",
            command: CommandSpec.CreateOpen(
                OrderType.MoveTo,
                new PositionCommandArgument(new NormalizedPosition(0.2f, 0.2f))),
            position: null,
            timing: new AfterActionTiming("cmd-a", ActionPhase.Completed),
            id: "cmd-b");

        var scenario = CreateScenario(triggerAction, dependentAction);

        var map = MapMetadataService.CreateForTesting(
            extentMin: Vector3.Zero,
            extentMax: new Vector3(100f, 100f, 20f),
            playerStarts: Vector3.Zero);

        var outputPath = Path.Combine(Path.GetTempPath(), $"rs-open-after-{Guid.NewGuid():N}.rep");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ReplayExporter.Export(scenario, map, outputPath, out _));

            Assert.Contains("cmd-b", ex.Message);
            Assert.Contains("cmd-a", ex.Message);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static ReplayScenario CreateScenario(params ActionEntry[] actions)
    {
        return new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            Players =
            {
                new PlayerSlotConfig
                {
                    Name = "Player1",
                    FactionIndex = 2,
                    Color = 1,
                    StartPosition = 1,
                    Team = 1,
                    Actions = actions.ToList(),
                },
            },
        };
    }
}
