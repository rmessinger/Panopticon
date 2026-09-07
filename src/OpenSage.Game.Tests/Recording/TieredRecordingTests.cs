using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Recording;
using OpenSage.Simulation;
using Xunit;

namespace OpenSage.Tests.Recording;

// A1-5 tiered recording: snapshot/delta reconstruction (WI-A1-11), T1 signature trees
// (WI-A1-09), T2 triggered detail capture (WI-A1-10), and the full-tier headless run
// (milestone: run reaches match end with no renderer/audio/window and writes T0/T1/T2).

public class TieredRecordingTests : MockedGameTest
{
    private static GameObject SpawnUnit(TestGame game, string name, Vector3 position, int ownerIndex = 0)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Infantry, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));
        var player = game.PlayerManager.GetPlayerByIndex((uint)ownerIndex);
        var obj = game.GameLogic.CreateObject(definition, player);
        obj.UpdateTransform(position, null);
        return obj;
    }

    private static (TestGame Game, SimulationHost Host) CreateHost()
    {
        var game = new TestGame(SageGame.CncGenerals);
        return (game, new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine)));
    }

    // ── WI-A1-09: T1 signature trees ──────────────────────────────────────────

    [Fact]
    public void SignatureTree_SameState_ProducesSameHashes()
    {
        var (game, host) = CreateHost();
        SpawnUnit(game, "A1SigUnitA", new Vector3(50, 50, 0));
        SpawnUnit(game, "A1SigUnitB", new Vector3(150, 50, 0));

        var sampler = new WorldStateSampler(host, game);
        var trees = new List<SignatureTreeFrame>();
        sampler.FrameSampled += (summary, _) => trees.Add(SignatureTreeBuilder.Build(summary.Frame, summary));
        host.Run(2);

        Assert.Equal(2, trees.Count);
        Assert.Equal(trees[0].RootHash, trees[1].RootHash);
        // Root plus two distinct spatial-cell buckets (100-unit grid).
        Assert.Equal(3, trees[0].Nodes.Count);
        Assert.Contains(trees[0].Nodes, n => n.Key == "cell:0,0");
        Assert.Contains(trees[0].Nodes, n => n.Key == "cell:1,0");
    }

    [Fact]
    public void SignatureTree_ToleranceQuantization()
    {
        var (game, host) = CreateHost();
        var unit = SpawnUnit(game, "A1ToleranceUnit", new Vector3(50, 50, 0));

        var sampler = new WorldStateSampler(host, game);
        var trees = new List<SignatureTreeFrame>();
        sampler.FrameSampled += (summary, _) => trees.Add(SignatureTreeBuilder.Build(summary.Frame, summary));
        host.Run(1);

        // Move below the position epsilon: hash unchanged.
        unit.UpdateTransform(new Vector3(50.2f, 50, 0), null);
        host.Run(1);
        Assert.Equal(trees[0].RootHash, trees[1].RootHash);

        // Move above the epsilon: hash changes.
        unit.UpdateTransform(new Vector3(75, 50, 0), null);
        host.Run(1);
        Assert.NotEqual(trees[1].RootHash, trees[2].RootHash);
    }

    [Fact]
    public void SignatureTree_StrictMode_HashesExactValues()
    {
        var (game, host) = CreateHost();
        var unit = SpawnUnit(game, "A1StrictUnit", new Vector3(50, 50, 0));

        var sampler = new WorldStateSampler(host, game);
        var trees = new List<SignatureTreeFrame>();
        var strict = new SignatureTolerances { Strict = true };
        sampler.FrameSampled += (summary, _) => trees.Add(SignatureTreeBuilder.Build(summary.Frame, summary, strict));
        host.Run(1);

        unit.UpdateTransform(new Vector3(50.2f, 50, 0), null);
        host.Run(1);

        Assert.NotEqual(trees[0].RootHash, trees[1].RootHash);
    }

    [Fact]
    public void SignatureTree_HashIndependentOfObjectOrder()
    {
        var a = new WorldStateSummary();
        a.Objects.Add(new WorldObjectState { ObjectId = 1, DefinitionName = "U", Position = new Vector3(10, 10, 0) });
        a.Objects.Add(new WorldObjectState { ObjectId = 2, DefinitionName = "U", Position = new Vector3(20, 10, 0) });

        var b = new WorldStateSummary();
        b.Objects.Add(new WorldObjectState { ObjectId = 2, DefinitionName = "U", Position = new Vector3(20, 10, 0) });
        b.Objects.Add(new WorldObjectState { ObjectId = 1, DefinitionName = "U", Position = new Vector3(10, 10, 0) });

        var treeA = SignatureTreeBuilder.Build(1, a);
        var treeB = SignatureTreeBuilder.Build(1, b);

        Assert.Equal(treeA.RootHash, treeB.RootHash);
        Assert.Equal(treeA.Nodes.Single(n => n.Key == "cell:0,0").Hash, treeB.Nodes.Single(n => n.Key == "cell:0,0").Hash);
    }

    [Fact]
    public void SignatureTreeComparer_LocalizesDivergenceToSubtree()
    {
        var (game, host) = CreateHost();
        SpawnUnit(game, "A1CmpUnit", new Vector3(50, 50, 0));
        SpawnUnit(game, "A1CmpUnit2", new Vector3(250, 50, 0));

        var sampler = new WorldStateSampler(host, game);
        SignatureTreeFrame? tree1 = null;
        SignatureTreeFrame? tree2 = null;
        var count = 0;
        sampler.FrameSampled += (summary, _) =>
        {
            var tree = SignatureTreeBuilder.Build(summary.Frame, summary);
            if (count == 0)
            {
                tree1 = tree;
            }
            if (count == 1)
            {
                tree2 = tree;
            }
            count++;
        };
        host.Run(1);

        // Diverge only the far cell.
        var farUnit = game.GameLogic.Objects.Single(o => o.Id.Index == 2);
        farUnit.UpdateTransform(new Vector3(260, 60, 0), null);
        host.Run(1);

        var diverged = SignatureTreeComparer.FindDivergedSubtrees(tree1, tree2);
        Assert.Contains("cell:2,0", diverged);
        Assert.DoesNotContain("cell:0,0", diverged);
    }

    // ── WI-A1-10: T2 triggered detail capture ─────────────────────────────────

    [Fact]
    public void TriggeredDetail_TripCapturesBoundedRingWindow()
    {
        var (game, host) = CreateHost();
        SpawnUnit(game, "A1T2Unit", new Vector3(50, 50, 0));

        var sampler = new WorldStateSampler(host, game);
        using var capture = new TriggeredDetailCapture(sampler, ringDepth: 10);
        var t1 = new TieredSignatureRecorder(sampler, sampleInterval: 5);
        t1.ArmTrigger(
            (frame, tree) => frame == 15 ? new[] { "world" } : null,
            capture);

        host.Run(20);

        var windows = capture.Complete();
        var window = Assert.Single(windows);
        Assert.Equal(15u, window.TriggeredAtFrame);
        Assert.Equal(10, window.Frames.Count); // bounded by ring depth
        Assert.Equal(6u, window.StartFrame);   // frames 6..15
        Assert.Equal(15u, window.EndFrame);
    }

    [Fact]
    public void TriggeredDetail_MarkedWindow_CollectsExactRange()
    {
        var (game, host) = CreateHost();
        SpawnUnit(game, "A1MarkUnit", new Vector3(50, 50, 0));

        var sampler = new WorldStateSampler(host, game);
        using var capture = TriggeredDetailCapture.WithMarkedWindow(sampler, startFrame: 5, endFrame: 8);

        host.Run(12);

        var window = Assert.Single(capture.Complete());
        Assert.True(window.Marked);
        Assert.Equal(5u, window.StartFrame);
        Assert.Equal(8u, window.EndFrame);
        Assert.Equal(4, window.Frames.Count);
    }

    // ── WI-A1-11: snapshot and delta recording ────────────────────────────────

    [Fact]
    public void SnapshotRecording_ReconstructsArbitraryFrame()
    {
        var (game, host) = CreateHost();
        var unitA = SpawnUnit(game, "A1SnapUnitA", new Vector3(50, 50, 0));
        SpawnUnit(game, "A1SnapUnitB", new Vector3(150, 50, 0));

        var sampler = new WorldStateSampler(host, game);
        using var recorder = new SnapshotDeltaRecorder(sampler, snapshotInterval: 10);

        WorldStateSummary? directAt40 = null;
        sampler.FrameSampled += (summary, _) =>
        {
            if (summary.Frame == 40)
            {
                directAt40 = summary;
            }
        };

        // Perturb the world mid-run so deltas are non-trivial.
        for (var i = 0; i < 45; i++)
        {
            unitA.UpdateTransform(new Vector3(50 + i, 50, 0), null);
            host.Run(1);
        }

        var reconstructed = recorder.ReconstructStateAt(40);
        Assert.NotNull(directAt40);
        Assert.Equal(directAt40!.Frame, reconstructed.Frame);
        Assert.Equal(
            directAt40.Objects.Select(o => (o.ObjectId, o.Position, o.Health, o.OwnerIndex)),
            reconstructed.Objects.Select(o => (o.ObjectId, o.Position, o.Health, o.OwnerIndex)));
        Assert.Equal(directAt40.PlayerMoney, reconstructed.PlayerMoney);
    }

    [Fact]
    public void SnapshotRecording_ArtifactRoundTrips()
    {
        var (game, host) = CreateHost();
        SpawnUnit(game, "A1ArtifactUnit", new Vector3(50, 50, 0));

        var path = Path.Combine(Path.GetTempPath(), $"opensage_snapshots_{Guid.NewGuid():N}.bin");
        try
        {
            var sampler = new WorldStateSampler(host, game);
            using var recorder = new SnapshotDeltaRecorder(sampler, snapshotInterval: 10, artifactPath: path, game: game);

            host.Run(25);

            var (snapshots, deltas) = WorldStateArtifact.Load(path, game);
            Assert.Equal(2, snapshots.Count); // frames 10, 20
            Assert.Equal(25, deltas.Count);

            var reconstructed = WorldStateArtifact.Reconstruct(snapshots, deltas, 15);
            Assert.Equal(15u, reconstructed.Frame);
            Assert.Equal(
                recorder.ReconstructStateAt(15).Objects.Select(o => (o.ObjectId, o.Position)),
                reconstructed.Objects.Select(o => (o.ObjectId, o.Position)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Milestone: full-tier headless run reaching match end ──────────────────

    [Fact]
    public void HeadlessRun_ReachesMatchEnd_AndWritesAllTiers()
    {
        // Run 1: baseline reference run.
        var referenceTrees = RunReferenceScenario();

        // Run 2: same setup plus an extra object, so T1 diverges from the reference at every
        // sampled frame and the armed T2 trigger captures windows. Runs to match end
        // (terminal GameOutcome) with no renderer/audio/window.
        var game = new TestGame(SageGame.CncGenerals);
        ConfigureTwoCompetitivePlayers(game);
        SpawnUnit(game, "A1MilestoneUnit", new Vector3(50, 50, 0));
        SpawnUnit(game, "A1MilestoneDivergence", new Vector3(250, 50, 0));

        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var outputDir = Path.Combine(Path.GetTempPath(), $"opensage_run_{Guid.NewGuid():N}");
        var snapshotPath = Path.Combine(outputDir, "snapshots.bin");
        using var recording = RunRecording.Attach(
            host, game, seed: 42, scenarioId: "a1-milestone",
            sampleInterval: 10, snapshotInterval: 10, snapshotArtifactPath: snapshotPath);
        recording.ArmReferenceTrigger(referenceTrees);

        host.Run(40);
        game.VictorySystem.Surrender(game.PlayerManager.Players[2]);
        var result = host.Run(60, h => game.CurrentOutcome.Status == OutcomeStatus.Decided);

        // Reached match end headlessly.
        Assert.True(result.StopPredicateTriggered);
        Assert.Equal(OutcomeStatus.Decided, game.CurrentOutcome.Status);

        var output = recording.Complete();
        Assert.Equal(42u, output.Seed);
        Assert.Equal(OutcomeStatus.Decided, output.Outcome!.Status);

        // T0: bounded metric series.
        Assert.NotEmpty(output.MetricSamples);

        // T1: sampled signature trees.
        Assert.NotEmpty(output.SignatureTrees);
        Assert.All(output.SignatureTrees, t => Assert.NotEqual(0UL, t.Nodes[0].Hash));

        // T2: the reference divergence tripped at the sampled frames. Cooldown is one ring
        // depth (30 frames), so trips land at frames 10 and 40; the decision right after ends
        // the run before any further trip. The divergence localizes to the extra unit's cell.
        Assert.Equal(2, output.TriggeredCaptures.Count);
        Assert.Equal(10u, output.TriggeredCaptures[0].TriggeredAtFrame);
        Assert.Contains("world", output.TriggeredCaptures[0].DivergedSubtrees!);
        Assert.Contains("cell:2,0", output.TriggeredCaptures[0].DivergedSubtrees!);
        Assert.Equal(1u, output.TriggeredCaptures[0].StartFrame);
        Assert.Equal(10u, output.TriggeredCaptures[0].EndFrame);
        Assert.Equal(10, output.TriggeredCaptures[0].Frames.Count);

        // Snapshots: artifact present, reloadable, and indexed.
        Assert.True(File.Exists(snapshotPath));
        Assert.Equal(new[] { 10u, 20u, 30u, 40u }, output.SnapshotFrames);
        var (snapshots, deltas) = WorldStateArtifact.Load(snapshotPath, game);
        Assert.Equal(4, snapshots.Count);
        Assert.True(deltas.Count >= (int)output.TotalLogicFrames - 1);

        // The whole output JSON round-trips.
        var jsonPath = Path.Combine(outputDir, "run.json");
        output.SaveToFile(jsonPath);
        var reloaded = RunOutput.LoadFromFile(jsonPath);
        Assert.NotNull(reloaded);
        Assert.Equal(output.TotalLogicFrames, reloaded!.TotalLogicFrames);
        Assert.Equal(output.SignatureTrees.Count, reloaded.SignatureTrees.Count);
        Assert.Equal(output.TriggeredCaptures.Count, reloaded.TriggeredCaptures.Count);

        Directory.Delete(outputDir, recursive: true);
    }

    private static Dictionary<uint, SignatureTreeFrame> RunReferenceScenario()
    {
        var game = new TestGame(SageGame.CncGenerals);
        ConfigureTwoCompetitivePlayers(game);
        SpawnUnit(game, "A1MilestoneUnit", new Vector3(50, 50, 0));

        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var sampler = new WorldStateSampler(host, game);
        using var t1 = new TieredSignatureRecorder(sampler, sampleInterval: 10);
        host.Run(50);
        return t1.SampledFrames.ToDictionary(t => t.Frame);
    }

    private static void ConfigureTwoCompetitivePlayers(TestGame game)
    {
        var mapPlayers = new[]
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
            new OpenSage.Data.Map.Player
            {
                Name = "player2",
                Faction = "FactionAmerica",
                DisplayName = "Player 2",
                IsHuman = true,
            },
            new OpenSage.Data.Map.Player
            {
                Name = "player3",
                Faction = "FactionChina",
                DisplayName = "Player 3",
                IsHuman = true,
            },
        };
        game.PlayerManager.OnNewGame(mapPlayers, GameType.Skirmish);
    }
}
