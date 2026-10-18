using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Recording;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests.Recording;

// WI-A5-07: RunOutput as a versioned, portable run-bundle manifest. Covers manifest-version
// rejection and legacy loading, bundle-relative artifact paths across moves, the frame-zero
// snapshot baseline, the recorded T1 signature configuration, and cross-run signature
// compatibility validation.

public class A5ManifestTests : MockedGameTest
{
    private static void SpawnUnit(TestGame game, string name, Vector3 position)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Infantry, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));
        var player = game.PlayerManager.GetPlayerByIndex(0);
        var obj = game.GameLogic.CreateObject(definition, player);
        obj.UpdateTransform(position, null);
    }

    private static (TestGame Game, SimulationHost Host) CreateHost()
    {
        var game = new TestGame(SageGame.CncGenerals);
        return (game, new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine)));
    }

    [Fact]
    public void FromJson_RejectsUnknownManifestVersion()
    {
        var json = """{ "manifestSchemaVersion": 99, "runId": "a5-reject" }""";

        var ex = Assert.Throws<InvalidOperationException>(() => RunOutput.FromJson(json));

        Assert.Contains("99", ex.Message);
        Assert.Contains("1", ex.Message);
    }

    [Fact]
    public void FromJson_NormalizesLegacyManifestWithoutVersion()
    {
        var json = """{ "runId": "a5-legacy" }""";

        var output = RunOutput.FromJson(json);

        Assert.NotNull(output);
        Assert.Equal("a5-legacy", output!.RunId);
        Assert.Equal(1, output.ManifestSchemaVersion);
    }

    [Fact]
    public void SaveToFile_MakesBundlePortableAcrossMoves()
    {
        var (game, host) = CreateHost();
        SpawnUnit(game, "A5PortableUnit", new Vector3(50, 50, 0));

        var bundleDir = Path.Combine(Path.GetTempPath(), $"a5-portable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundleDir);
        try
        {
            var manifestPath = Path.Combine(bundleDir, "run.json");
            using (var recording = RunRecording.Attach(
                host, game, seed: 42, scenarioId: "a5-portable",
                snapshotInterval: 2, snapshotArtifactPath: Path.Combine(bundleDir, "snapshots.bin")))
            {
                host.Run(5);
                recording.Complete().SaveToFile(manifestPath);
            }

            var movedDir = bundleDir + "-moved";
            Directory.Move(bundleDir, movedDir);
            try
            {
                var loaded = RunOutput.LoadFromFile(Path.Combine(movedDir, "run.json"));

                // The stored relative path resolved against the manifest's new location.
                Assert.NotNull(loaded.SnapshotArtifactPath);
                Assert.True(File.Exists(loaded.SnapshotArtifactPath));
                Assert.Equal(movedDir, Path.GetDirectoryName(loaded.SnapshotArtifactPath));

                var (snapshots, _) = WorldStateArtifact.Load(loaded.SnapshotArtifactPath!, game);
                Assert.Contains(snapshots, s => s.Frame == 0);
            }
            finally
            {
                Directory.Delete(movedDir, recursive: true);
            }
        }
        finally
        {
            if (Directory.Exists(bundleDir))
            {
                Directory.Delete(bundleDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveToFile_KeepsPathsOutsideTheBundleAbsolute()
    {
        var rootA = Path.Combine(Path.GetTempPath(), $"a5-outside-a-{Guid.NewGuid():N}");
        var rootB = Path.Combine(Path.GetTempPath(), $"a5-outside-b-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        try
        {
            var output = new RunOutput { RunId = "a5-outside", SnapshotArtifactPath = Path.Combine(rootA, "snapshots.bin") };

            var manifestPath = Path.Combine(rootB, "run.json");
            output.SaveToFile(manifestPath);

            // Stored verbatim: the artifact is not under the manifest directory.
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.Equal(
                Path.Combine(rootA, "snapshots.bin"),
                document.RootElement.GetProperty("snapshotArtifactPath").GetString());
            Assert.Equal(
                Path.Combine(rootA, "snapshots.bin"),
                RunOutput.LoadFromFile(manifestPath).SnapshotArtifactPath);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Fact]
    public void Attach_CapturesFrameZeroSnapshotBeforeCadence()
    {
        var (game, host) = CreateHost();
        SpawnUnit(game, "A5FrameZeroUnit", new Vector3(50, 50, 0));

        var path = Path.Combine(Path.GetTempPath(), $"a5-framezero-{Guid.NewGuid():N}.bin");
        try
        {
            using (var recording = RunRecording.Attach(
                host, game, seed: 42, scenarioId: "a5-frame-zero",
                snapshotInterval: 1000, snapshotArtifactPath: path))
            {
                host.Run(3);

                var output = recording.Complete();

                Assert.NotEmpty(output.SnapshotFrames);
                Assert.Equal(0u, output.SnapshotFrames[0]);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Complete_RecordsT1SignatureConfiguration()
    {
        var (game, host) = CreateHost();
        SpawnUnit(game, "A5T1ConfigUnit", new Vector3(50, 50, 0));

        using (var recording = RunRecording.Attach(host, game, seed: 42, scenarioId: "a5-t1-config"))
        {
            host.Run(2);

            var output = recording.Complete();

            Assert.NotNull(output.T1Configuration);
            Assert.Equal(SignatureTreeBuilder.DefaultCellSize, output.T1Configuration!.CellSize);
            Assert.Equal(SignatureTolerances.Default.PositionEpsilon, output.T1Configuration.PositionEpsilon);
            Assert.Equal(SignatureTolerances.Default.HealthEpsilon, output.T1Configuration.HealthEpsilon);
            Assert.Equal("domain", output.T1Configuration.BucketKeys);
        }
    }

    [Fact]
    public void ValidateSignatureConfigurationCompatible_DetectsConfigurationDrift()
    {
        var baseline = new RunOutput
        {
            RunId = "a5-compat-baseline",
            T1Configuration = new RunSignatureConfigurationRecord
            {
                CellSize = SignatureTreeBuilder.DefaultCellSize,
                BucketKeys = "domain",
            },
        };
        var sameConfiguration = new RunOutput
        {
            RunId = "a5-compat-same",
            T1Configuration = new RunSignatureConfigurationRecord
            {
                CellSize = SignatureTreeBuilder.DefaultCellSize,
                BucketKeys = "domain",
            },
        };
        var drifted = new RunOutput
        {
            RunId = "a5-compat-drifted",
            T1Configuration = new RunSignatureConfigurationRecord
            {
                CellSize = SignatureTreeBuilder.DefaultCellSize * 2,
                BucketKeys = "domain",
            },
        };

        baseline.ValidateSignatureConfigurationCompatible(sameConfiguration);

        var ex = Assert.Throws<InvalidOperationException>(
            () => baseline.ValidateSignatureConfigurationCompatible(drifted));
        Assert.Contains("a5-compat-baseline", ex.Message);
        Assert.Contains("a5-compat-drifted", ex.Message);
    }

    [Fact]
    public void ValidateSignatureConfigurationCompatible_AcceptsLegacyRunsWithoutConfiguration()
    {
        var legacyA = new RunOutput { RunId = "a5-legacy-a" };
        var legacyB = new RunOutput { RunId = "a5-legacy-b" };

        legacyA.ValidateSignatureConfigurationCompatible(legacyB);
    }

    [Fact]
    public void MakePortable_AndResolveBundlePath_RoundTripInsideBundle()
    {
        var bundleDir = Path.Combine(Path.GetTempPath(), "a5-path-bundle");
        var artifact = Path.Combine(bundleDir, "snapshots.bin");
        var manifest = Path.Combine(bundleDir, "run.json");

        var portable = RunOutput.MakePortable(artifact, Path.GetDirectoryName(manifest)!);
        Assert.Equal("snapshots.bin", portable);

        var resolved = RunOutput.ResolveBundlePath(manifest, portable);
        Assert.Equal(Path.GetFullPath(artifact), resolved);
    }
}
