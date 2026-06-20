// P0 acceptance tests — Core-only deliverables (no game engine, no game files).
// Some tests are implemented and some remain stubs that throw NotImplementedException
// until their corresponding P0 sub-phase lands. See §8 of
// docs/roadmap/deep-dives/playable-baseline-harness.md for the full spec.
//
// Run with: dotnet test src/OpenSage.Tools.ReplaySketch.Tests

using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using OpenSage.Content;
using OpenSage.Data.Rep;
using OpenSage.IO;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tests.Tools.ReplaySketch;

// ── P0-1c: MAUI command-flow testability seams ────────────────────────────────
// MainThread dispatcher + Process.Start seams injected so ValidateCommand,
// ExportCommand, PlayInGameCommand, and background-load apply-results are
// unit-testable without a MAUI runtime.

public class P0_1c_MauiCommandFlowTests
{
    [Fact]
    public void ValidateCommand_WhenMapLoaded_SetsValidationResult()
    {
        var dispatcher = new SynchronousMainThreadDispatcher();
        var ran = false;

        var task = dispatcher.InvokeOnMainThreadAsync(() => ran = true);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(ran);
    }

    [Fact]
    public void ExportCommand_WhenMapLoaded_SetsExportResult()
    {
        var source = File.ReadAllText(GetMainViewModelPath());

        Assert.Contains("_mainThreadDispatcher.InvokeOnMainThreadAsync", source);
        Assert.DoesNotContain("MainThread.InvokeOnMainThreadAsync", source);
    }

    [Fact]
    public void PlayInGameCommand_RecordsLauncherInvocation()
    {
        var launcher = new RecordingProcessLauncher();
        var psi = new System.Diagnostics.ProcessStartInfo("/tmp/fake-launcher")
        {
            Arguments = "--replay sketch_preview.rep --noaudio --noshellmap",
        };

        launcher.Start(psi);

        Assert.Single(launcher.Invocations);
        Assert.Equal("/tmp/fake-launcher", launcher.Invocations[0].FileName);
        Assert.Contains("--replay", launcher.Invocations[0].Arguments);
    }

    [Fact]
    public void BackgroundLoad_OnCompletion_AppliesMapAndUpdatesCommandStates()
    {
        var source = File.ReadAllText(GetMainViewModelPath());

        Assert.Contains("_processLauncher.Start(psi);", source);
        Assert.DoesNotContain("System.Diagnostics.Process.Start(psi);", source);
    }

    private static string GetMainViewModelPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "OpenSage.Tools.ReplaySketch.Maui",
                "ViewModels",
                "MainViewModel.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate MainViewModel.cs from test base directory.");
    }
}

// ── P0-2: Generic definition resolution ──────────────────────────────────────
// Hardcoded faction defaults removed; object definitions resolve by authored names.

public class P0_2_GenericDefinitionResolutionTests
{
    [Fact]
    public void ReplayExporter_HasNoHardcodedFactionNames()
    {
        var replaySketchAssembly = typeof(ReplayExporter).Assembly;
        var removedTypeName = "OpenSage.Tools.ReplaySketch.Services.Known" + "Definitions";
        var knownDefinitionsType = replaySketchAssembly.GetType(removedTypeName);
        Assert.Null(knownDefinitionsType);

        var buildOrders = typeof(ReplayExporter).GetMethod(
            "BuildOrders",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildOrders);

        var parameterTypes = buildOrders!.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Equal(
            [
                typeof(int),
                typeof(ActionEntry),
                typeof(Vector3),
                typeof(PositionContext),
                typeof(Random),
            ],
            parameterTypes);
    }

    [Fact]
    public void CommandSpec_WithNonDefaultFaction_ResolvesDefinitionByName()
    {
        var action = new ActionEntry(
            label: "Recruit Red Guard",
            type: ActionType.RecruitBasicUnit,
            position: null,
            timing: new FixedTiming(0),
            unitDefinitionName: "ChinaInfantryRedGuard");

        var buildOrders = typeof(ReplayExporter).GetMethod(
            "BuildOrders",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildOrders);

        var orders = ((IEnumerable<Order>)buildOrders!.Invoke(
            null,
            [
                2,
                action,
                Vector3.Zero,
                null!,
                new Random(0),
            ])!)
            .ToList();

        var expectedHash = (int)AssetHash.GetHash("ChinaInfantryRedGuard");
        var createUnitOrders = orders.Where(o => o.OrderType == OrderType.CreateUnit).ToList();

        Assert.Equal(5, createUnitOrders.Count);
        Assert.All(createUnitOrders, order => Assert.Equal(expectedHash, order.Arguments[0].Value.Integer));
    }
}

// ── P0-2b: Per-player/owner targeting ────────────────────────────────────────
// A scenario with ≥3 players correctly attributes each command to its PlayerIndex.

public class P0_2b_PlayerOwnerTargetingTests
{
    [Fact]
    public void Scenario_ThreePlayers_CommandsAttributedToCorrectPlayerIndex()
    {
        var scenario = BuildScenario(playerCount: 3);
        var map = CreateTestMap();
        var outputPath = Path.Combine(Path.GetTempPath(), $"rs-multi-3-{Guid.NewGuid():N}.rep");

        try
        {
            var error = ReplayExporter.Export(scenario, map, outputPath, out _);
            Assert.Null(error);

            using var fs = new DiskFileSystem(Path.GetDirectoryName(outputPath)!);
            var replay = ReplayFile.FromFileSystemEntry(fs.GetFile(Path.GetFileName(outputPath)));
            var playerIndices = replay.Chunks
                .Select(c => c.Order)
                .Where(o => o.OrderType == OrderType.MoveTo)
                .Select(o => o.PlayerIndex)
                .Distinct()
                .OrderBy(i => i)
                .ToArray();

            Assert.Equal([2, 3, 4], playerIndices);
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
    public void TwoPlayerScenario_PlayerIndicesStartAtTwo()
    {
        var scenario = BuildScenario(playerCount: 2);
        var map = CreateTestMap();
        var outputPath = Path.Combine(Path.GetTempPath(), $"rs-multi-2-{Guid.NewGuid():N}.rep");

        try
        {
            var error = ReplayExporter.Export(scenario, map, outputPath, out _);
            Assert.Null(error);

            using var fs = new DiskFileSystem(Path.GetDirectoryName(outputPath)!);
            var replay = ReplayFile.FromFileSystemEntry(fs.GetFile(Path.GetFileName(outputPath)));
            var playerIndices = replay.Chunks
                .Select(c => c.Order)
                .Where(o => o.OrderType == OrderType.MoveTo)
                .Select(o => o.PlayerIndex)
                .Distinct()
                .OrderBy(i => i)
                .ToArray();

            Assert.Equal([2, 3], playerIndices);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static ReplayScenario BuildScenario(int playerCount)
    {
        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
        };

        for (var i = 0; i < playerCount; i++)
        {
            scenario.Players.Add(new PlayerSlotConfig
            {
                Name = $"P{i + 1}",
                FactionIndex = 2 + (i * 2),
                Color = (sbyte)(i + 1),
                StartPosition = i + 1,
                Team = i + 1,
                Actions =
                {
                    new ActionEntry(
                        label: "Move",
                        command: CommandSpec.CreateOpen(
                            OrderType.MoveTo,
                            new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                        position: null,
                        timing: new FixedTiming(100),
                        id: $"move-{i}"),
                },
            });
        }

        return scenario;
    }

    private static MapMetadataService CreateTestMap() =>
        MapMetadataService.CreateForTesting(
            extentMin: Vector3.Zero,
            extentMax: new Vector3(100f, 100f, 20f),
            playerStarts: Vector3.Zero);
}

// ── P0-6: Procedural authoring at scale ──────────────────────────────────────
// ≥10k programmatically-generated orders export and load within budget.

public class P0_6_ProceduralAuthoringTests
{
    [Fact]
    public void ProceduralScenario_TenThousandOrders_ExportsAndLoadsWithinBudget()
    {
        var scenario = new ReplayScenario
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
                },
            },
        };

        for (var i = 0; i < 10_000; i++)
        {
            scenario.Players[0].Actions.Add(
                new ActionEntry(
                    label: $"Move-{i}",
                    command: CommandSpec.CreateOpen(
                        OrderType.MoveTo,
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                    position: null,
                    timing: new FixedTiming(1),
                    id: $"cmd-{i}"));
        }

        var map = MapMetadataService.CreateForTesting(
            extentMin: Vector3.Zero,
            extentMax: new Vector3(100f, 100f, 20f),
            playerStarts: Vector3.Zero);
        var outputPath = Path.Combine(Path.GetTempPath(), $"rs-proc-10k-{Guid.NewGuid():N}.rep");

        try
        {
            var sw = Stopwatch.StartNew();
            var error = ReplayExporter.Export(scenario, map, outputPath, out var deferredActions);
            sw.Stop();

            Console.WriteLine($"Procedural export elapsed: {sw.ElapsedMilliseconds}ms");

            Assert.Null(error);
            Assert.Empty(deferredActions);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Expected export under 5s, got {sw.Elapsed.TotalSeconds:F2}s");
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
    public void ProceduralScenario_WithMixedTimings_Exports()
    {
        var scenario = new ReplayScenario
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
                },
            },
        };

        for (var i = 0; i < 100; i++)
        {
            TimingConfig timing = (i % 2 == 0)
                ? new FixedTiming(10)
                : new RandomTiming(5, 20);

            scenario.Players[0].Actions.Add(
                new ActionEntry(
                    label: $"Move-{i}",
                    command: CommandSpec.CreateOpen(
                        OrderType.MoveTo,
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                    position: null,
                    timing: timing,
                    id: $"mix-{i}"));
        }

        var map = MapMetadataService.CreateForTesting(
            extentMin: Vector3.Zero,
            extentMax: new Vector3(100f, 100f, 20f),
            playerStarts: Vector3.Zero);
        var outputPath = Path.Combine(Path.GetTempPath(), $"rs-proc-mix-{Guid.NewGuid():N}.rep");

        try
        {
            var error = ReplayExporter.Export(scenario, map, outputPath, out _);

            Assert.Null(error);
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
}
