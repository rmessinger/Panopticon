using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Recording;

// WI-A1-21: the operational run bundle. Full mode never creates a bundle; Operational mode with
// a bundle directory writes run.json + operational.bin + checkpoints/, keeps a manifest on
// incomplete disposal, and produces stream-shape- and hash-identical bundles across same-seed
// runs. Checkpoint capture needs a fully booted game, so the bundle tests run against the
// installed skirmish boot (GameFact), not the mocked TestGame.

[Collection("SerialGameTests")]
public class OperationalRunBundleTests : MockedGameTest
{
    [Fact]
    public void Full_WithBundleDirectory_CreatesNoBundle()
    {
        var bundle = Path.Combine(Path.GetTempPath(), "a1-21-full-" + Guid.NewGuid().ToString("N"));
        var game = new TestGame(SageGame.CncGenerals);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));

        using (var recording = RunRecording.Attach(
                   host, game, seed: 31u, scenarioId: "a1-21-full",
                   options: new RecordingOptions { Mode = RecordingMode.Full, BundleDirectory = bundle }))
        {
            host.Run(6);
            Assert.Null(recording.Operational);
            var output = recording.Complete();
            Assert.False(Directory.Exists(bundle));
            Assert.Null(output.RecordingMode);
            Assert.Null(output.OperationalStreamPath);
            Assert.Null(output.Checkpoints);
            Assert.DoesNotContain("recordingMode", output.ToJson(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("operationalStreamPath", output.ToJson(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void Operational_WritesBundleLayoutManifestAndCheckpoints()
    {
        var bundle = RunOperationalBundle(out var output, injectOrder: true);

        Assert.Equal(RecordingMode.Operational, output.RecordingMode);
        Assert.Equal("operational.bin", output.OperationalStreamPath);
        Assert.NotNull(output.Checkpoints);
        Assert.Equal(new uint[] { 0, 15, 30, 45 }, output.Checkpoints.Select(c => c.Frame).ToArray());

        var manifest = RunOutput.FromJson(File.ReadAllText(Path.Combine(bundle, "run.json")));
        Assert.NotNull(manifest);
        Assert.Equal(output.RunId, manifest.RunId);
        Assert.Equal(RecordingMode.Operational, manifest.RecordingMode);
        Assert.Equal(output.Checkpoints.Select(c => c.Frame), manifest.Checkpoints!.Select(c => c.Frame));

        foreach (var checkpoint in output.Checkpoints!)
        {
            var fullPath = Path.Combine(bundle, checkpoint.Path);
            Assert.True(File.Exists(fullPath), $"missing {checkpoint.Path}");
            var bytes = File.ReadAllBytes(fullPath);
            Assert.Equal((long)bytes.Length, checkpoint.Bytes);
            Assert.Equal(EngineCheckpoint.ContentHash(bytes), checkpoint.XxHash3);
        }

        using (var stream = File.OpenRead(Path.Combine(bundle, "operational.bin")))
        using (var reader = OperationalStreamReader.Open(stream, Generals))
        {
            Assert.Equal(output.RunId, reader.RunId);
            var index = reader.BuildScanIndex();
            Assert.Equal(new uint[] { 5, 10, 15, 20, 25, 30, 35, 40, 45 }, index.Select(e => e.Frame).ToArray());

            var sawInputs = false;
            foreach (var entry in index)
            {
                var (_, records) = reader.ReadChunk(entry);
                Assert.Contains(records, r => r is OperationalTick);
                Assert.Contains(records, r => r is OperationalWorldDelta);
                Assert.DoesNotContain(records, r => r is OperationalDomainDelta or OperationalDomainSnapshot or OperationalRollups or OperationalEvents);
                foreach (var record in records)
                {
                    if (record is OperationalInputs inputs && inputs.Orders.Count > 0)
                    {
                        sawInputs = true;
                    }
                }
            }

            Assert.True(sawInputs, "no chunk carried the injected order");
        }

        using (var checkpointStream = File.OpenRead(Path.Combine(bundle, "checkpoints/000000015.ckpt")))
        {
            var checkpoint = EngineCheckpoint.Read(checkpointStream);
            Assert.Equal(15u, checkpoint.Header.Frame);
            Assert.Contains(EngineCheckpointData.EngineSection, checkpoint.Sections.Keys);
            Assert.Contains(EngineCheckpointData.RandomSection, checkpoint.Sections.Keys);
            Assert.Contains(EngineCheckpointData.HostSection, checkpoint.Sections.Keys);
            Assert.DoesNotContain(EngineCheckpointData.DomainStateSection, checkpoint.Sections.Keys);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void Operational_SecondRun_ProducesIdenticalStreamShape()
    {
        var firstBundle = RunOperationalBundle(out var first, injectOrder: true);
        var secondBundle = RunOperationalBundle(out var second, injectOrder: true);

        Assert.Equal(first.Checkpoints!.Select(c => c.Frame), second.Checkpoints!.Select(c => c.Frame));
        // Checkpoint bytes differ across runs (each header carries its own run id), so equality
        // is asserted on the stream shape: boundary frames and record kinds.
        Assert.NotEqual(first.RunId, second.RunId);

        var firstShapes = ReadChunkShapes(firstBundle);
        var secondShapes = ReadChunkShapes(secondBundle);
        Assert.Equal(firstShapes.Count, secondShapes.Count);
        for (var i = 0; i < firstShapes.Count; i++)
        {
            Assert.Equal(firstShapes[i].Frame, secondShapes[i].Frame);
            Assert.Equal(firstShapes[i].RecordTypes, secondShapes[i].RecordTypes);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void Operational_DisposeWithoutComplete_KeepsManifestAndScannableStream()
    {
        var bundle = Path.Combine(Path.GetTempPath(), "a1-21-crash-" + Guid.NewGuid().ToString("N"));
        using var game = BootGame();
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var cadence = new RecordingCadence(frame => frame % 5 == 0, frame => frame % 15 == 0);

        using (var recording = RunRecording.Attach(
                   host, game, seed: 33u, scenarioId: "a1-21-crash",
                   options: new RecordingOptions { Mode = RecordingMode.Operational, BundleDirectory = bundle, DefaultCadence = cadence }))
        {
            host.Run(20);
        }

        var manifest = RunOutput.FromJson(File.ReadAllText(Path.Combine(bundle, "run.json")));
        Assert.NotNull(manifest);
        // The manifest is written at attach and rewritten only on Complete, so a crashed run
        // keeps the attach-time checkpoint list; the frame-15 checkpoint file still exists.
        Assert.Equal(new uint[] { 0 }, manifest.Checkpoints!.Select(c => c.Frame).ToArray());
        Assert.True(File.Exists(Path.Combine(bundle, "checkpoints/000000015.ckpt")));

        using (var stream = File.OpenRead(Path.Combine(bundle, "operational.bin")))
        using (var reader = OperationalStreamReader.Open(stream, Generals))
        {
            Assert.Null(reader.TryReadFooterIndex());
            Assert.Equal(new uint[] { 5, 10, 15, 20 }, reader.BuildScanIndex().Select(e => e.Frame).ToArray());
        }
    }

    private static Game BootGame()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        Platform.Start();
        var game = new Game(installation, null, new Configuration
        {
            NoAudio = true,
            SkipStartingBuildings = true,
        }, null);
        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.Player, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
        };
        game.StartSkirmishOrMultiPlayerGame(
            "maps/alpine assault/alpine assault.map",
            new EchoConnection(),
            playerSettings,
            seed: 12345,
            isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        return game;
    }

    private static string RunOperationalBundle(out RunOutput output, bool injectOrder)
    {
        var bundle = Path.Combine(Path.GetTempPath(), "a1-21-bundle-" + Guid.NewGuid().ToString("N"));
        using var game = BootGame();
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var cadence = new RecordingCadence(frame => frame % 5 == 0, frame => frame % 15 == 0);

        using (var recording = RunRecording.Attach(
                   host, game, seed: 32u, scenarioId: "a1-21-bundle",
                   options: new RecordingOptions { Mode = RecordingMode.Operational, BundleDirectory = bundle, DefaultCadence = cadence }))
        {
            host.Run(10);
            if (injectOrder)
            {
                host.InjectOrders(12, [Order.CreateMoveOrder(1, new Vector3(4, 5, 6))]);
            }

            host.Run(36);
            output = recording.Complete();
        }

        return bundle;
    }

    private List<(uint Frame, List<OperationalRecordType> RecordTypes)> ReadChunkShapes(string bundle)
    {
        using var stream = File.OpenRead(Path.Combine(bundle, "operational.bin"));
        using var reader = OperationalStreamReader.Open(stream, Generals);
        var shapes = new List<(uint, List<OperationalRecordType>)>();
        foreach (var entry in reader.BuildScanIndex())
        {
            var (frame, records) = reader.ReadChunk(entry);
            shapes.Add((frame, records.Select(r => r.Type).ToList()));
        }

        return shapes;
    }
}
