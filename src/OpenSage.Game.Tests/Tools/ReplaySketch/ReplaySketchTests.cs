using System;
using System.IO;
using System.Linq;
using OpenSage.Input;
using OpenSage.IO;
using OpenSage.Mathematics;
using OpenSage.Tests.Data;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ReplaySketchTests
{
    // Uncomment to run locally (requires game files):
    // private const string LocalOnlySkip = "local debug only";
    private const string? LocalOnlySkip = null;

    [Fact]
    public void DefaultScenario_UsaActions_AreInExpectedOrder()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var usa = scenario.Players[0];

        Assert.Equal(4, usa.Actions.Count);
        Assert.Equal(ActionType.BuildBarracks, usa.Actions[0].Type);
        Assert.Equal(ActionType.GatherResources, usa.Actions[1].Type);
        Assert.Equal(ActionType.RecruitBasicUnit, usa.Actions[2].Type);
        Assert.Equal(ActionType.AttackEnemyBase, usa.Actions[3].Type);
    }

    [Fact]
    public void DefaultScenario_GlaActions_AreInExpectedOrder()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var gla = scenario.Players[1];

        Assert.Equal(4, gla.Actions.Count);
        Assert.Equal(ActionType.BuildBarracks, gla.Actions[0].Type);
        Assert.Equal(ActionType.GatherResources, gla.Actions[1].Type);
        Assert.Equal(ActionType.RecruitBasicUnit, gla.Actions[2].Type);
        Assert.Equal(ActionType.AttackEnemyBase, gla.Actions[3].Type);
    }

    [Fact]
    public void DefaultScenario_GatherResources_IsScheduledAfterBarracksConstruction()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var rng = new Random(0);

        foreach (var player in scenario.Players)
        {
            var barracksFrame = player.Actions[0].Timing.Resolve(rng);
            var gatherOffset = player.Actions[1].Timing.Resolve(rng);

            Assert.Equal(ActionType.BuildBarracks, player.Actions[0].Type);
            Assert.Equal(ActionType.GatherResources, player.Actions[1].Type);
            Assert.True(barracksFrame > 0, $"{player.Name}: BuildBarracks must be scheduled at a non-zero frame.");
            Assert.True(gatherOffset > 0, $"{player.Name}: GatherResources must fire after BuildBarracks (positive offset).");
        }
    }

    [Fact]
    public void DefaultScenario_CumulativeFrames_AreMonotonicallyIncreasing()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var rng = new Random(0);

        foreach (var player in scenario.Players)
        {
            uint cumulative = 0;
            uint previous = 0;
            for (var i = 0; i < player.Actions.Count; i++)
            {
                cumulative += player.Actions[i].Timing.Resolve(rng);
                Assert.True(cumulative > previous,
                    $"{player.Name}: action {i} ({player.Actions[i].Type}) must fire at a later frame than the previous action.");
                previous = cumulative;
            }
        }
    }

    /// <summary>
    /// Exports the default Alpine Assault scenario as a replay and runs it through
    /// the full game engine from startup to the last replay frame.
    /// Runs headlessly (no window, no SDL2) using an offscreen graphics device.
    /// Uncomment <see cref="LocalOnlySkip"/> to run locally with game files installed.
    /// </summary>
    [GameFact(SageGame.CncGenerals, Skip = LocalOnlySkip)]
    public void DefaultScenario_RunsToCompletion_InFullGame()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        var map = MapMetadataService.Load(installation, scenario.MapPath);
        var outputPath = Path.Combine(Path.GetTempPath(), $"opensage_replaysketch_{Guid.NewGuid()}.rep");

        try
        {
            var exportError = ReplayExporter.Export(scenario, map, outputPath);
            Assert.Null(exportError);

            // Run headlessly: pass null for window so the engine creates an offscreen
            // graphics device (Metal on macOS, Vulkan on Linux, D3D11 on Windows).
            // This avoids SDL2's macOS main-thread requirement entirely.
            using var game = new Game(installation, null, new Configuration { NoAudio = true }, null);

            // Speed the replay up so that the 1 050-frame scenario (≈ 210 game-seconds
            // at 5 Hz) completes in a few real-world seconds instead of three minutes.
            game.LogicUpdateScaleFactor = 50f;

            using var repFileSystem = new DiskFileSystem(Path.GetDirectoryName(outputPath)!);
            var repEntry = repFileSystem.GetFile(Path.GetFileName(outputPath));

            game.LoadReplayFile(repEntry);
            game.StartRun();

            var dispatchedCount = 0;
            var totalExpectedDispatches = scenario.Players.Sum(p => p.Actions.Count);

            using var monitor = new ReplayActionMonitor(game);
            monitor.ActionDispatched += (_, e) =>
            {
                Console.WriteLine($"Action dispatched: {e.ActionType} (player: {e.Player?.Name ?? "?"})");
                dispatchedCount++;
            };
            monitor.ActionCompleted += (_, e) =>
                Console.WriteLine($"Action completed:  {e.ActionType} (player: {e.Player?.Name ?? "?"})");

            var frame = new Rectangle(0, 0, 1024, 768);
            var deadline = DateTime.UtcNow.AddSeconds(60);

            while (game.IsRunning && dispatchedCount < totalExpectedDispatches && DateTime.UtcNow < deadline)
            {
                game.Update(Array.Empty<InputMessage>());
                game.Panel.EnsureFrame(frame);
                game.Render();
            }

            Assert.Equal(totalExpectedDispatches, dispatchedCount);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}
